[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$tourPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\GuidedTourWindow.cs'
$appTourPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\App.GuidedTour.cs'
$helpPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\HelpViewModel.cs'
$appXamlPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\App.xaml'

foreach ($path in @($tourPath, $appTourPath, $helpPath, $appXamlPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing guided-tour file: $path"
    }
}

$tour = Get-Content -LiteralPath $tourPath -Raw
$appTour = Get-Content -LiteralPath $appTourPath -Raw
$help = Get-Content -LiteralPath $helpPath -Raw
$appXaml = Get-Content -LiteralPath $appXamlPath -Raw

$requiredDestinations = @(
    'Dashboard',
    'Sites',
    'WordPress Explorer',
    'Content Audit',
    'SEO Audit',
    'Broken Links',
    'Suggested Changes',
    'Approval Queue',
    'Backups',
    'Execution Center',
    'Reports'
)

foreach ($destination in $requiredDestinations) {
    if (-not $tour.Contains('"' + $destination + '"')) {
        throw "Guided tour is missing required destination: $destination"
    }
}

$requiredTourTokens = @(
    'GuidedTourStateStore.Load()',
    'GuidedTourStateStore.Save(',
    'OpenCurrentStep()',
    '_viewModel.NavigateCommand.Execute',
    'ValidateStep(',
    'TourGate.SiteSelected',
    'TourGate.Synchronized',
    'TourGate.AuditCompleted',
    'TourGate.SuggestionsGenerated',
    'TourGate.ApprovalCompleted',
    'TourGate.ExecutionCompleted',
    'TourGate.VerificationCompleted',
    '_next.IsEnabled = validation.IsComplete',
    'Check step',
    'Site → Sync → Audit → Recommend → Approve → Backup → Execute → Verify'
)
foreach ($token in $requiredTourTokens) {
    if (-not $tour.Contains($token)) {
        throw "Guided tour is missing workflow contract: $token"
    }
}

foreach ($token in @('ShowGuidedTour(bool restart = false)', 'App_OnActivated(', 'GuidedTourStateStore.Reset()')) {
    if (-not $appTour.Contains($token)) {
        throw "App guided-tour integration is missing: $token"
    }
}

foreach ($token in @('ResumeGuidedTourCommand', 'RestartGuidedTourCommand', 'App.ShowGuidedTour(restart)')) {
    if (-not $help.Contains($token)) {
        throw "Help center is missing guided-tour access: $token"
    }
}

if (-not $appXaml.Contains('Activated="App_OnActivated"')) {
    throw 'App.xaml does not trigger the post-login guided tour activation hook.'
}

Write-Host 'Guided tour workflow contracts validated successfully.' -ForegroundColor Green
