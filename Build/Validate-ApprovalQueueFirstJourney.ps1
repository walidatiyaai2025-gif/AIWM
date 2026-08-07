[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\SuggestedChangesViewModel.ApprovalJourney.cs'
$experiencePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ApprovalQueueFirstJourneyExperience.cs'
$gatePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ApprovalQueueJourneyGateCoordinator.cs'
$sidebarPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\MainWindowViewModel.FirstJourneySidebar.cs'

foreach ($path in @($statePath, $experiencePath, $gatePath, $sidebarPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing Approval Queue journey file: $path" }
}

$state = Get-Content -LiteralPath $statePath -Raw
$experience = Get-Content -LiteralPath $experiencePath -Raw
$gate = Get-Content -LiteralPath $gatePath -Raw
$sidebar = Get-Content -LiteralPath $sidebarPath -Raw

foreach ($token in @(
    'using AIWordPressManager.Application.Changes;',
    'IReadOnlyList<SuggestedChangeItem>',
    'ApprovalJourneyRequirements',
    'IsApprovalJourneyReady',
    'RefreshApprovalJourneyReadinessAsync',
    '_service.GetAsync(lease.SiteId, null)',
    'ApprovalPendingCount',
    'ApprovalApprovedCount',
    'ApprovalRejectedCount',
    'ExecutionReadyCount',
    '!string.IsNullOrWhiteSpace(item.ObjectId)',
    'item.CanApplyDirectly || item.RequiresStaging',
    'Approved change',
    'Execution plan verified'
)) {
    if (-not $state.Contains($token)) { throw "Approval readiness state is missing token: $token" }
}

foreach ($token in @(
    'STEP 5 · APPROVE EXECUTION QUEUE',
    'ApprovalJourneyStatus',
    'ApprovalJourneyRequirements',
    'BulkApproveCommand',
    'Continue to Execution Center',
    'SuggestedChanges.IsApprovalJourneyReady',
    'ApprovalPendingCount',
    'ApprovalApprovedCount',
    'ApprovalRejectedCount',
    'RefreshSafeAsync',
    'catch (Exception exception)',
    '_refreshRunning',
    '_closed'
)) {
    if (-not $experience.Contains($token)) { throw "Approval Queue journey UI is missing token: $token" }
}

if ($experience.Contains('new Action(async')) {
    throw 'Approval Queue experience must not use an async-void Dispatcher callback.'
}

foreach ($token in @(
    'RefreshApprovalJourneyReadinessAsync',
    'ApplyApprovalQueueJourneyGate',
    'CurrentJourneyTarget = "Approval Queue"',
    '!main.SuggestedChanges.IsFirstJourneyReady',
    'main.SuggestedChanges.IsApprovalJourneyReady',
    'ApplyGateSafeAsync',
    'catch (Exception exception)',
    'Debug.WriteLine',
    '_refreshPending = false;'
)) {
    if (-not $gate.Contains($token)) { throw "Approval Queue gate is missing token: $token" }
}

if ($gate.Contains('new Action(async')) {
    throw 'Approval Queue gate must not use an async-void Dispatcher callback.'
}

foreach ($token in @(
    '!SuggestedChanges.IsApprovalJourneyReady',
    '"Approval Queue" => SuggestedChanges.IsApprovalJourneyReady',
    '"Execution Center"'
)) {
    if (-not $sidebar.Contains($token)) { throw "First journey sidebar is missing Approval Queue token: $token" }
}

Write-Host 'Approval Queue startup safety, decisions, imports, exception-safe refresh, navigation gate and sidebar contracts validated successfully.' -ForegroundColor Green
