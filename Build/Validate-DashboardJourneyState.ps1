[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolverPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\JourneyStateResolver.cs'
$journeyPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\CompleteUserJourneyExperience.cs'

if (-not (Test-Path -LiteralPath $resolverPath)) {
    throw "Missing journey resolver: $resolverPath"
}

if (-not (Test-Path -LiteralPath $journeyPath)) {
    throw "Missing dashboard journey experience: $journeyPath"
}

$resolver = Get-Content -LiteralPath $resolverPath -Raw
$journey = Get-Content -LiteralPath $journeyPath -Raw

$requiredStages = @(
    '"Site"',
    '"Sync"',
    '"Analyze"',
    '"Recommend"',
    '"Approve"',
    '"Backup"',
    '"Execute"',
    '"Verify"',
    '"Complete"'
)

foreach ($stage in $requiredStages) {
    if (-not $resolver.Contains($stage)) {
        throw "Canonical journey stage is missing: $stage"
    }
}

$requiredTargets = @(
    '"Sites"',
    '"WordPress Explorer"',
    '"SEO Audit"',
    '"Suggested Changes"',
    '"Approval Queue"',
    '"Backup & Restore"',
    '"Execution Center"',
    '"Evidence Center"'
)

foreach ($target in $requiredTargets) {
    if (-not $resolver.Contains($target)) {
        throw "Journey target is missing: $target"
    }
}

if (-not $resolver.Contains('JourneyStageStatus.Blocked')) {
    throw 'Blocked journey state is not implemented.'
}

if (-not $resolver.Contains('IsArabic')) {
    throw 'Arabic journey localization contract is not implemented.'
}

if (-not $journey.Contains('ContinueJourneyCommand')) {
    throw 'The dashboard journey card has no primary next-action command binding.'
}

Write-Host 'Dashboard journey state contracts validated successfully.' -ForegroundColor Green
