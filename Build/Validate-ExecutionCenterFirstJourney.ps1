[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\ExecutionCenterViewModel.FirstJourney.cs'
$experiencePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ExecutionCenterFirstJourneyExperience.cs'
$gatePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ExecutionCenterJourneyGateCoordinator.cs'
$receiptPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\ExecutionCenterViewModel.Receipts.cs'
$centerPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\ExecutionCenterViewModel.cs'

foreach ($path in @($statePath, $experiencePath, $gatePath, $receiptPath, $centerPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing Execution Center first journey contract file: $path" }
}

$state = Get-Content -LiteralPath $statePath -Raw
$experience = Get-Content -LiteralPath $experiencePath -Raw
$gate = Get-Content -LiteralPath $gatePath -Raw
$receipt = Get-Content -LiteralPath $receiptPath -Raw
$center = Get-Content -LiteralPath $centerPath -Raw

foreach ($token in @(
    'FirstJourneyRequirements', 'IsFirstJourneyReady', 'HasTerminalExecutionState', 'HasExecutionReceipt',
    'HasExecutionEvidence', 'ExecutedCount > 0', 'LastExecutionUtc is not null', 'File.Exists(LatestReceiptPath)',
    'Approved queue loaded', 'Verified execution', 'Terminal result', 'Execution receipt', 'Backup and evidence'
)) {
    if (-not $state.Contains($token)) { throw "Execution readiness is missing contract token: $token" }
}

foreach ($token in @(
    'STEP 6 · EXECUTE AND PRESERVE RECEIPT', 'ExecutionCenter.FirstJourneyStatus',
    'ExecutionCenter.FirstJourneyRequirements', 'ExecutionCenter.LoadCommand', 'ExecutionCenter.ExecuteSelectedCommand',
    'ExecutionCenter.OpenLatestReceiptCommand', 'Continue to Evidence Center', 'ExecutionCenter.IsFirstJourneyReady'
)) {
    if (-not $experience.Contains($token)) { throw "Execution journey UI is missing contract token: $token" }
}

foreach ($token in @(
    'ExecutionCenterJourneyGateCoordinator', 'ExecutionCenter.RefreshFirstJourneyReadiness',
    'SuggestedChanges.IsApprovalJourneyReady', 'ApplyExecutionCenterJourneyGate', 'CurrentJourneyTarget = "Execution Center"',
    'private bool _isApplying;', 'if (_isApplying) return;', 'finally', '_isApplying = false;',
    'nameof(ExecutionCenterViewModel.QueueState)', 'nameof(ExecutionCenterViewModel.LatestReceiptPath)',
    'nameof(ExecutionCenterViewModel.BeforeEvidencePath)', 'nameof(ExecutionCenterViewModel.AfterEvidencePath)'
)) {
    if (-not $gate.Contains($token)) { throw "Execution journey gate is missing contract token: $token" }
}

if ($gate.Contains('private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => ApplyGate();')) {
    throw 'Execution journey gate must filter PropertyChanged events to prevent recursive readiness refresh.'
}

foreach ($token in @('WriteExecutionReceiptSafeAsync', 'LatestReceiptPath', 'IsReceiptTerminalState', 'ExecutionReceiptDocument')) {
    if (-not $receipt.Contains($token)) { throw "Execution receipt workflow is missing contract token: $token" }
}

foreach ($token in @('ExecuteSelectedCommand', 'ExecuteAllReadyCommand', 'RollbackSelectedCommand', 'BeforeEvidencePath', 'AfterEvidencePath')) {
    if (-not $center.Contains($token)) { throw "Execution Center is missing workflow contract: $token" }
}

Write-Host 'Execution Center first journey, reentrancy protection, terminal state, evidence, and receipt contracts validated successfully.' -ForegroundColor Green
