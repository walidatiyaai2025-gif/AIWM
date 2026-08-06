[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\MainWindowViewModel.FirstJourneyCompletion.cs'
$experiencePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\FirstJourneyCompletionExperience.cs'
$evidencePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\EvidenceCenterViewModel.FirstJourney.cs'

foreach ($path in @($statePath, $experiencePath, $evidencePath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing journey completion contract file: $path" }
}

$state = Get-Content -LiteralPath $statePath -Raw
$experience = Get-Content -LiteralPath $experiencePath -Raw
$evidence = Get-Content -LiteralPath $evidencePath -Raw

foreach ($token in @(
    'IsFirstJourneyCompleted',
    'Sites.IsFirstJourneyReady',
    'Explorer.IsFirstJourneyReady',
    'SeoAudit.IsFirstJourneyReady',
    'SuggestedChanges.IsApprovalJourneyReady',
    'ExecutionCenter.IsFirstJourneyReady',
    'EvidenceCenter.IsFirstJourneyReady',
    'FirstJourneyCompletionTitle',
    'FirstJourneyCompletionSummary',
    'FirstJourneyCompletionReceipt',
    'FirstJourneyCompletionEvidence',
    'OpenCompletedJourneyReceiptCommand',
    'RefreshCompletedJourneyCommand'
)) {
    if (-not $state.Contains($token)) { throw "Journey completion state is missing token: $token" }
}

foreach ($token in @(
    'FIRST JOURNEY RESULT',
    'FirstJourneyCompletionSummary',
    'Open final receipt',
    'Refresh verification',
    'Review Evidence Center',
    'main.IsFirstJourneyCompleted',
    'FirstJourneyCompletionSummary',
    '_refreshing'
)) {
    if (-not $experience.Contains($token)) { throw "Journey completion UI is missing token: $token" }
}

if (-not $evidence.Contains('IsFirstJourneyReady')) {
    throw 'Evidence Center does not expose final journey readiness.'
}

Write-Host 'First journey completion summary, receipt, evidence, navigation and refresh contracts validated successfully.' -ForegroundColor Green
