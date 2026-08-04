$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$desktop = Join-Path $root 'src\AIWordPressManager.Desktop'
$failures = New-Object System.Collections.Generic.List[string]

Get-ChildItem $desktop -Recurse -Filter *.cs | ForEach-Object {
    $text = Get-Content $_.FullName -Raw
    if ($_.Name -ne 'SitesViewModel.cs' -and $text -match '\bSitesViewModel\b') {
        $failures.Add("ViewModel-to-ViewModel site dependency: $($_.FullName)")
    }
    if ($text -match '\b_sites\b') {
        $failures.Add("Legacy _sites dependency: $($_.FullName)")
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}
Write-Host 'Guided-flow contracts validated: all feature screens use the current-site context.' -ForegroundColor Green
