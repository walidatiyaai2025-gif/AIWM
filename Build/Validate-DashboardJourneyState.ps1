[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolverPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\JourneyStateResolver.cs'
$journeyPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\CompleteUserJourneyExperience.cs'
$bindingPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\CanonicalJourneyStateBindingExperience.cs'

foreach ($path in @($resolverPath, $journeyPath, $bindingPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing dashboard journey contract file: $path"
    }
}

$resolver = Get-Content -LiteralPath $resolverPath -Raw
$journey = Get-Content -LiteralPath $journeyPath -Raw
$binding = Get-Content -LiteralPath $bindingPath -Raw

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

$requiredResolverContracts = @(
    'JourneyStageStatus.Blocked',
    'IsArabic',
    'HasBackup',
    'HasFailure',
    'CanRollback'
)

foreach ($contract in $requiredResolverContracts) {
    if (-not $resolver.Contains($contract)) {
        throw "Journey resolver contract is missing: $contract"
    }
}

$requiredBindingContracts = @(
    'JourneyStateResolver.Resolve',
    'RefreshCanonicalJourneyState',
    'DashboardLastSiteSync',
    'DashboardSeoScoreState',
    'DashboardAiSuggestions',
    'JourneyApprovalState',
    'JourneyExecuteState',
    'JourneyVerifyState',
    'CompleteJourneyContextText',
    'CurrentJourneyTarget = result.Target',
    'DispatcherPriority.ContextIdle'
)

foreach ($contract in $requiredBindingContracts) {
    if (-not $binding.Contains($contract)) {
        throw "Dashboard journey binding contract is missing: $contract"
    }
}

if (-not $journey.Contains('ContinueJourneyCommand')) {
    throw 'The dashboard journey card has no primary next-action command binding.'
}

Write-Host 'Dashboard journey resolver and binding contracts validated successfully.' -ForegroundColor Green
