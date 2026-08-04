param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "[1/6] Removing generated build folders..." -ForegroundColor Cyan
Get-ChildItem -Path $root -Directory -Recurse -Force |
    Where-Object { $_.Name -in @('bin','obj') } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "[2/6] Restoring packages..." -ForegroundColor Cyan
dotnet restore .\AIWordPressManager.sln

Write-Host "[3/6] Building solution..." -ForegroundColor Cyan
dotnet build .\AIWordPressManager.sln -c $Configuration --no-restore

if (-not $SkipTests) {
    Write-Host "[4/6] Running tests..." -ForegroundColor Cyan
    dotnet test .\AIWordPressManager.sln -c $Configuration --no-build
} else {
    Write-Host "[4/6] Tests skipped by request." -ForegroundColor Yellow
}

Write-Host "[5/6] Checking for accidental generated artifacts in source folders..." -ForegroundColor Cyan
$bad = Get-ChildItem -Path $root -File -Recurse -Force |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Extension -in @('.dll','.pdb') }
if ($bad) {
    $bad | ForEach-Object { Write-Warning "Unexpected binary in source tree: $($_.FullName)" }
}

Write-Host "[6/6] Validation completed successfully." -ForegroundColor Green
