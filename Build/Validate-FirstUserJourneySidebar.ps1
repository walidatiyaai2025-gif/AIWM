[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\MainWindowViewModel.FirstJourneySidebar.cs'
$bootstrapPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\FirstUserJourneySidebarBootstrap.cs'
$dashboardJourneyPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\CompleteUserJourneyExperience.cs'

foreach ($path in @($statePath, $bootstrapPath, $dashboardJourneyPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing first-user-journey file: $path" }
}

$state = Get-Content -LiteralPath $statePath -Raw
$bootstrap = Get-Content -LiteralPath $bootstrapPath -Raw
$dashboardJourney = Get-Content -LiteralPath $dashboardJourneyPath -Raw

$definitionsStart = $state.IndexOf('var definitions = new[]', [StringComparison]::Ordinal)
$definitionsEnd = $state.IndexOf('FirstJourneySidebarPages.Clear();', [StringComparison]::Ordinal)
if ($definitionsStart -lt 0 -or $definitionsEnd -le $definitionsStart) {
    throw 'First journey sidebar definitions block could not be located.'
}
$orderedState = $state.Substring($definitionsStart, $definitionsEnd - $definitionsStart)

$orderedTargets = @(
    '"Dashboard"',
    '"Sites"',
    '"WordPress Explorer"',
    '"SEO Audit"',
    '"Suggested Changes"',
    '"Approval Queue"',
    '"Execution Center"',
    '"Evidence Center"'
)

$lastIndex = -1
foreach ($target in $orderedTargets) {
    $index = $orderedState.IndexOf($target, [StringComparison]::Ordinal)
    if ($index -lt 0) { throw "First journey sidebar target is missing: $target" }
    if ($index -le $lastIndex) { throw "First journey sidebar target order is invalid at: $target" }
    $lastIndex = $index
}

foreach ($token in @(
    'FirstJourneySidebarPages',
    'RefreshFirstJourneySidebar',
    'CompleteJourneySteps',
    'NavigateCommand',
    'StatusIcon',
    'StatusBrush',
    'FirstJourneySidebarSummary'
)) {
    if (-not $state.Contains($token)) { throw "First journey state is missing contract token: $token" }
}

foreach ($token in @(
    'FIRST USER JOURNEY',
    'FirstUserJourneySidebar',
    'FindNavigationButton(window, "Dashboard")',
    'FindNavigationButton(window, "Sites")',
    'FindSharedVerticalPanel',
    'FirstJourneySidebarPages',
    'FirstJourneySidebarSummary',
    'Button.CommandProperty',
    'Button.CommandParameterProperty',
    'CompleteJourneySteps.CollectionChanged',
    'CurrentPage',
    'CurrentJourneyTarget'
)) {
    if (-not $bootstrap.Contains($token)) { throw "First journey sidebar bootstrap is missing contract token: $token" }
}

foreach ($token in @('CompleteJourneySteps', 'ContinueJourneyCommand', 'RefreshCompleteUserJourney')) {
    if (-not $dashboardJourney.Contains($token)) { throw "Dashboard first-journey foundation is missing: $token" }
}

Write-Host 'Dashboard-first ordered user journey sidebar contracts validated successfully.' -ForegroundColor Green
