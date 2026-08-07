[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$runnerPath = Join-Path $repoRoot "Build/Invoke-WindowsAcceptance.ps1"
$workflowPath = Join-Path $repoRoot ".github/workflows/stability-build.yml"

foreach ($path in @($runnerPath, $workflowPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Windows acceptance contract file is missing: $path"
    }
}

$runner = Get-Content -LiteralPath $runnerPath -Raw
$workflow = Get-Content -LiteralPath $workflowPath -Raw

foreach ($token in @(
    'Smoke-Test-DesktopStartup.ps1',
    'windows-acceptance.json',
    'windows-acceptance.md',
    'Get-FileHash',
    'SHA256',
    'SourceBranch',
    'SourceCommit',
    'DesktopFileVersion',
    'DesktopSha256',
    'SmokeOutput',
    'Failure',
    'Windows acceptance evidence completed successfully.'
)) {
    if (-not $runner.Contains($token)) {
        throw "Windows acceptance runner is missing contract token: $token"
    }
}

foreach ($token in @(
    'Validate Windows acceptance evidence',
    'Generate Windows acceptance evidence',
    'Invoke-WindowsAcceptance.ps1',
    'windows-acceptance-results',
    'AcceptanceResults',
    'if: always()'
)) {
    if (-not $workflow.Contains($token)) {
        throw "Stability workflow is missing Windows acceptance token: $token"
    }
}

Write-Host "Windows acceptance runner, evidence, hash, build identity, and CI artifact contracts validated successfully." -ForegroundColor Green
