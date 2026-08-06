[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\EvidenceCenterViewModel.FirstJourney.cs'
$receiptPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\EvidenceCenterViewModel.Receipts.cs'
$experiencePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\EvidenceCenterFirstJourneyExperience.cs'

foreach ($path in @($statePath, $receiptPath, $experiencePath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing Evidence Center first journey file: $path" }
}

$state = Get-Content -LiteralPath $statePath -Raw
$receipts = Get-Content -LiteralPath $receiptPath -Raw
$experience = Get-Content -LiteralPath $experiencePath -Raw

foreach ($token in @(
    'FirstJourneyRequirements',
    'IsFirstJourneyReady',
    'ReceiptCount',
    'BeforeCount > 0',
    'AfterCount > 0',
    'VerifiedPairCount > 0',
    'File.Exists'
)) {
    if (-not $state.Contains($token)) { throw "Evidence readiness is missing token: $token" }
}

foreach ($token in @(
    'AIWordPressManager',
    'Receipts',
    'ExecutionReceipt_*.html',
    'ExecutionReceipt_*.json',
    'MergeExecutionReceipts',
    'DistinctBy',
    'OrderByDescending'
)) {
    if (-not $receipts.Contains($token)) { throw "Evidence receipt integration is missing token: $token" }
}

foreach ($token in @(
    'STEP 7 · VERIFY AND COMPLETE JOURNEY',
    'EvidenceCenter.FirstJourneyStatus',
    'Refresh evidence',
    'Open selected',
    'Open evidence folder',
    'Journey completed',
    'EvidenceCenter.IsFirstJourneyReady',
    '_isRefreshing'
)) {
    if (-not $experience.Contains($token)) { throw "Evidence journey UI is missing token: $token" }
}

Write-Host 'Evidence Center receipts, before/after pairing, final verification and journey completion contracts validated successfully.' -ForegroundColor Green
