param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    Write-Host "`n=== $Name ===" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE." }
}

Invoke-Step 'Validate XAML resources' {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Validate-XamlResources.ps1')
}

Invoke-Step 'Validate Desktop contracts' {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Validate-DesktopContracts.ps1')
}

$bridgeValidator = Join-Path $PSScriptRoot 'Validate-WordPressBridge.ps1'
if (Test-Path $bridgeValidator) {
    Invoke-Step 'Validate WordPress Bridge package' {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $bridgeValidator
    }
}

Invoke-Step 'Restore solution' { dotnet restore .\AIWordPressManager.sln }
Invoke-Step 'Build solution' { dotnet build .\AIWordPressManager.sln -c $Configuration --no-restore }

if (-not $SkipTests) {
    Invoke-Step 'Run tests' { dotnet test .\AIWordPressManager.sln -c $Configuration --no-build }
}

if ($Configuration -eq 'Release') {
    $setupProject = '.\Setup\AIWordPressManager.Setup.csproj'
    if (Test-Path $setupProject) {
        Invoke-Step 'Build Setup EXE' { dotnet build $setupProject -c Release }
    }
}

$manifestDir = Join-Path $root 'Files\ReleaseReadiness'
New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
$manifest = Join-Path $manifestDir ("release-validation-{0}.txt" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
@(
    "AI WordPress Manager Enterprise Release Validation",
    "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    "Configuration: $Configuration",
    "Result: PASSED",
    "Machine: $env:COMPUTERNAME",
    "User: $env:USERNAME"
) | Set-Content -Path $manifest -Encoding UTF8

Write-Host "`nRELEASE VALIDATION PASSED" -ForegroundColor Green
Write-Host "Manifest: $manifest"
