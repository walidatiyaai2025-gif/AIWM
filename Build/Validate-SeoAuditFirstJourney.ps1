[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\SeoAuditViewModel.FirstJourney.cs'
$experiencePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\SeoAuditFirstJourneyExperience.cs'
$gatePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\SeoAuditJourneyGateCoordinator.cs'
$auditPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\SeoAuditViewModel.cs'
$sidebarPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\MainWindowViewModel.FirstJourneySidebar.cs'

foreach ($path in @($statePath, $experiencePath, $gatePath, $auditPath, $sidebarPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing SEO Audit first journey contract file: $path"
    }
}

$state = Get-Content -LiteralPath $statePath -Raw
$experience = Get-Content -LiteralPath $experiencePath -Raw
$gate = Get-Content -LiteralPath $gatePath -Raw
$audit = Get-Content -LiteralPath $auditPath -Raw
$sidebar = Get-Content -LiteralPath $sidebarPath -Raw

foreach ($token in @(
    'FirstJourneyRequirements',
    'FirstJourneyStatus',
    'FirstJourneyCompletedAt',
    'IsFirstJourneyReady',
    'RefreshFirstJourneyReadiness',
    'AuditedItems > 0',
    'Score is >= 0 and <= 100',
    'HighIssues + MediumIssues + LowIssues == Issues.Count',
    'History.Count > 0',
    'Synchronized site',
    'Audited content',
    'Measurable score',
    'Issue classification',
    'Saved baseline'
)) {
    if (-not $state.Contains($token)) {
        throw "SEO Audit readiness state is missing contract token: $token"
    }
}

foreach ($token in @(
    'SeoAuditFirstJourneyExperience',
    'STEP 3 · BUILD SEO BASELINE',
    'SeoAudit.FirstJourneyStatus',
    'SeoAudit.FirstJourneyRequirements',
    'SeoAudit.RunAuditCommand',
    'Continue to Suggested Changes',
    'CommandParameter = "Suggested Changes"',
    'SeoAudit.IsFirstJourneyReady',
    'FindButtonForCommand',
    'ReferenceEquals(button.Command, expectedCommand)',
    'Score {0}/100',
    'SeoAudit.History.CollectionChanged'
)) {
    if (-not $experience.Contains($token)) {
        throw "SEO Audit journey UI is missing contract token: $token"
    }
}

foreach ($token in @(
    'SeoAuditJourneyGateCoordinator',
    'ApplySeoAuditJourneyGate',
    'Build SEO baseline',
    'Open SEO Audit',
    'CurrentJourneyTarget = "SEO Audit"',
    '!main.Sites.IsFirstJourneyReady',
    '!main.Explorer.IsFirstJourneyReady',
    'main.SeoAudit.IsFirstJourneyReady'
)) {
    if (-not $gate.Contains($token)) {
        throw "SEO Audit journey gate is missing contract token: $token"
    }
}

foreach ($token in @(
    'LoadLatestAsync',
    'LoadHistoryAsync',
    'RunAuditCommand',
    '_service.RunAsync(siteId)',
    'Apply(result.Value',
    'History.Add(point)',
    'HighIssues',
    'MediumIssues',
    'LowIssues'
)) {
    if (-not $audit.Contains($token)) {
        throw "SEO Audit page is missing baseline workflow contract: $token"
    }
}

foreach ($token in @(
    '!SeoAudit.IsFirstJourneyReady',
    '"SEO Audit" => SeoAudit.IsFirstJourneyReady',
    '"Suggested Changes"'
)) {
    if (-not $sidebar.Contains($token)) {
        throw "First journey sidebar is missing SEO Audit readiness token: $token"
    }
}

Write-Host 'SEO Audit first journey baseline, persistence, issue classification, gate and navigation contracts validated successfully.' -ForegroundColor Green
