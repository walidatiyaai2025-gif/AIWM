param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host '[1/5] Closing stale build server processes...' -ForegroundColor Cyan
dotnet build-server shutdown | Out-Host

Write-Host '[2/5] Removing stale bin/obj folders...' -ForegroundColor Cyan
Get-ChildItem -Path $root -Directory -Recurse -Force |
    Where-Object { $_.Name -in @('bin','obj') } |
    Sort-Object FullName -Descending |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host '[3/5] Clearing local NuGet caches...' -ForegroundColor Cyan
dotnet nuget locals all --clear | Out-Host

Write-Host '[4/5] Restoring solution packages...' -ForegroundColor Cyan
dotnet restore .\AIWordPressManager.sln --force --no-cache | Out-Host

Write-Host '[5/5] Building the complete solution...' -ForegroundColor Cyan
dotnet build .\AIWordPressManager.sln -c $Configuration --no-restore | Out-Host

Write-Host "Build completed successfully: $Configuration" -ForegroundColor Green
