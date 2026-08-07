[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\WordPressExplorerViewModel.FirstJourney.cs'
$experiencePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ExplorerFirstJourneyExperience.cs'
$gatePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ExplorerJourneyGateCoordinator.cs'
$explorerPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\WordPressExplorerViewModel.cs'

foreach ($path in @($statePath, $experiencePath, $gatePath, $explorerPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing Explorer journey contract file: $path" }
}

$state = Get-Content -LiteralPath $statePath -Raw
$experience = Get-Content -LiteralPath $experiencePath -Raw
$gate = Get-Content -LiteralPath $gatePath -Raw
$explorer = Get-Content -LiteralPath $explorerPath -Raw

foreach ($token in @(
    'FirstJourneyRequirements', 'FirstJourneyStatus', 'IsFirstJourneyReady',
    'RefreshFirstJourneyReadiness', 'LoadedAt.HasValue', 'TotalPosts > 0 || TotalPages > 0',
    'TotalCategories > 0 || TotalTags > 0', 'Media inventory checked',
    'WordPress snapshot is complete and ready for the first SEO Audit'
)) {
    if (-not $state.Contains($token)) { throw "Explorer readiness contract is missing: $token" }
}

foreach ($token in @(
    'STEP 2 · COMPLETE WORDPRESS SNAPSHOT', 'Explorer.FirstJourneyStatus',
    'Explorer.FirstJourneyRequirements', 'Explorer.ProgressPercent',
    'Explorer.RefreshCommand', 'Explorer.CancelCommand', 'Continue to SEO Audit',
    'BuildButton("Continue to SEO Audit", "NavigateCommand", "SEO Audit"', 'Explorer.IsFirstJourneyReady',
    'FindButtonForCommand', 'ReferenceEquals(button.Command, expected)'
)) {
    if (-not $experience.Contains($token)) { throw "Explorer journey UI contract is missing: $token" }
}

foreach ($token in @(
    'ExplorerJourneyGateCoordinator', 'main.Explorer.RefreshFirstJourneyReadiness()',
    'main.RefreshFirstJourneySidebar()', 'ApplyExplorerJourneyGate',
    'Complete WordPress snapshot', 'Open WordPress Explorer',
    'CurrentJourneyTarget = "WordPress Explorer"'
)) {
    if (-not $gate.Contains($token)) { throw "Explorer central gate contract is missing: $token" }
}

foreach ($token in @(
    'IOfflineSnapshotService', 'LoadAsync()', 'SynchronizeNowAsync()',
    '_offline.LoadAsync(siteId)', '_sync.SynchronizeAsync', 'WordPressSyncProgress',
    'ApplySnapshot(result.Value)', 'Online sync failed. Offline data remains available',
    'TotalPosts', 'TotalPages', 'TotalCategories', 'TotalTags', 'TotalMedia'
)) {
    if (-not $explorer.Contains($token)) { throw "Explorer offline/synchronization contract is missing: $token" }
}

Write-Host 'WordPress Explorer first-journey snapshot, offline cache, progress and SEO gate contracts validated successfully.' -ForegroundColor Green
