[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\SuggestedChangesViewModel.FirstJourney.cs'
$experiencePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\SuggestedChangesFirstJourneyExperience.cs'
$gatePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\SuggestedChangesJourneyGateCoordinator.cs'
$viewModelPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\SuggestedChangesViewModel.cs'
$sidebarPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\MainWindowViewModel.FirstJourneySidebar.cs'

foreach ($path in @($statePath, $experiencePath, $gatePath, $viewModelPath, $sidebarPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing Suggested Changes journey contract file: $path" }
}

$state = Get-Content -LiteralPath $statePath -Raw
$experience = Get-Content -LiteralPath $experiencePath -Raw
$gate = Get-Content -LiteralPath $gatePath -Raw
$viewModel = Get-Content -LiteralPath $viewModelPath -Raw
$sidebar = Get-Content -LiteralPath $sidebarPath -Raw

foreach ($token in @(
    'FirstJourneyRequirements',
    'FirstJourneyStatus',
    'IsFirstJourneyReady',
    'FirstJourneyReviewedCount',
    'FirstJourneyRiskClassifiedCount',
    'FirstJourneyRoutedCount',
    'CurrentValue',
    'ProposedValue',
    'RiskLevel',
    'CanApplyDirectly',
    'RequiresStaging',
    'PendingCount > 0',
    'Generated proposals',
    'Before / after review',
    'Risk classification',
    'Execution routing',
    'Approval candidates'
)) {
    if (-not $state.Contains($token)) { throw "Suggested Changes readiness state is missing: $token" }
}

foreach ($token in @(
    'STEP 4 · REVIEW SUGGESTED CHANGES',
    'SuggestedChanges.GenerateCommand',
    'SuggestedChanges.RefreshCommand',
    'SuggestedChanges.FirstJourneyRequirements',
    'SuggestedChanges.IsFirstJourneyReady',
    'Continue to Approval Queue',
    'CommandParameter = "Approval Queue"',
    'FindButtonForCommand',
    'ReferenceEquals(button.Command, expectedCommand)'
)) {
    if (-not $experience.Contains($token)) { throw "Suggested Changes journey UI is missing: $token" }
}

foreach ($token in @(
    'ApplySuggestedChangesJourneyGate',
    'SuggestedChanges.IsFirstJourneyReady',
    'SeoAudit.IsFirstJourneyReady',
    'CurrentJourneyTarget = "Suggested Changes"'
)) {
    if (-not $gate.Contains($token)) { throw "Suggested Changes journey gate is missing: $token" }
}

foreach ($token in @(
    'GenerateFromLocalInsightsAsync',
    'SetApprovalStatusAsync',
    'SelectedExecutionPlan',
    'SelectedExpectedResult',
    'HighRiskCount',
    'StagingCount',
    'ShowApprovalQueueAsync'
)) {
    if (-not $viewModel.Contains($token)) { throw "Suggested Changes workflow is missing: $token" }
}

foreach ($token in @(
    '!SuggestedChanges.IsFirstJourneyReady',
    '"Suggested Changes" => SuggestedChanges.IsFirstJourneyReady'
)) {
    if (-not $sidebar.Contains($token)) { throw "Sidebar is not driven by Suggested Changes readiness: $token" }
}

Write-Host 'Suggested Changes proposal generation, before/after review, risk routing and approval readiness contracts validated successfully.' -ForegroundColor Green
