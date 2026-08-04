param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\src\AIWordPressManager.Desktop"))
)

$ErrorActionPreference = "Stop"

Write-Host "Validating XAML and runtime resource keys under $ProjectRoot" -ForegroundColor Cyan

$xamlFiles = Get-ChildItem -Path $ProjectRoot -Recurse -Filter *.xaml
$codeFiles = Get-ChildItem -Path $ProjectRoot -Recurse -Include *.cs
$declared = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
$references = New-Object 'System.Collections.Generic.List[object]'

foreach ($file in $xamlFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw

    [regex]::Matches($content, 'x:Key\s*=\s*"([^"]+)"') | ForEach-Object {
        [void]$declared.Add($_.Groups[1].Value)
    }

    [regex]::Matches($content, '\{(StaticResource|DynamicResource)\s+([^\},\s]+)') | ForEach-Object {
        $references.Add([pscustomobject]@{
            File = $file.FullName
            Kind = $_.Groups[1].Value
            Key  = $_.Groups[2].Value
        })
    }
}

foreach ($file in $codeFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    [regex]::Matches($content, '(?:FindResource|TryFindResource)\(\s*"([^"]+)"\s*\)') | ForEach-Object {
        $references.Add([pscustomobject]@{
            File = $file.FullName
            Kind = 'RuntimeResource'
            Key  = $_.Groups[1].Value
        })
    }
}

$frameworkKeys = @(
    'SystemAccentColor',
    'SystemControlHighlightAccentBrush'
)

$missing = $references |
    Where-Object { -not $declared.Contains($_.Key) -and $_.Key -notin $frameworkKeys } |
    Sort-Object File, Kind, Key -Unique

if ($missing.Count -gt 0) {
    Write-Host "Missing resource keys:" -ForegroundColor Red
    $missing | Format-Table -AutoSize
    exit 1
}

Write-Host "Resource validation passed. StaticResource, DynamicResource, and runtime lookups are valid." -ForegroundColor Green
