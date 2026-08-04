$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$desktop = Join-Path $root 'src\AIWordPressManager.Desktop'

if (-not (Test-Path $desktop)) { throw "Desktop project not found: $desktop" }

$errors = New-Object System.Collections.Generic.List[string]
$files = Get-ChildItem $desktop -Recurse -Filter *.cs

foreach ($file in $files) {
    $text = Get-Content $file.FullName -Raw
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName)

    $usesIo = $text -match '\b(File|Path|Directory|DirectoryInfo|SearchOption)\b'
    $hasIo = $text -match '(?m)^using System\.IO;\s*$'
    if ($usesIo -and -not $hasIo) {
        $errors.Add("$relative uses System.IO types without 'using System.IO;'.")
    }

    if ($text -match '\.ShowMessageAsync\s*\(') {
        $errors.Add("$relative calls obsolete IDialogService.ShowMessageAsync; use ShowInformationAsync or ShowErrorAsync.")
    }

    if ($text -match '\.LocalDataDirectory\b') {
        $errors.Add("$relative uses obsolete IApplicationPathService.LocalDataDirectory; use GetApplicationDataDirectory().")
    }

    if ($text -match 'new\s+AsyncRelayCommand\s*\(\s*RefreshAsync\s*,') {
        $errors.Add("$relative passes a method with optional parameters directly to AsyncRelayCommand; wrap it in a parameterless lambda.")
    }

    if ($text -match 'result\.Error\s*\?\?') {
        $errors.Add("$relative treats Result.Error as a string; use result.Error.Message.")
    }
}

if ($errors.Count -gt 0) {
    Write-Host "Desktop contract validation failed:" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "Desktop contract validation passed for $($files.Count) C# files." -ForegroundColor Green

# Part 136: UiOperationService namespace contract
$uiOperationViolations = Get-ChildItem -Path $desktopRoot -Recurse -Filter *.cs | Where-Object {
    $content = Get-Content $_.FullName -Raw
    $content -match '(?<!Services\.)\bUiOperationService\b' -and
    $content -notmatch 'using\s+AIWordPressManager\.Desktop\.Services\s*;' -and
    $content -notmatch 'namespace\s+AIWordPressManager\.Desktop\.Services'
}
if ($uiOperationViolations) {
    Write-Host 'UiOperationService namespace contract failed:' -ForegroundColor Red
    $uiOperationViolations.FullName | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}
