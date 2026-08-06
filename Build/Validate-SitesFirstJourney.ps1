[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\Sites\SitesViewModel.FirstJourney.cs'
$experiencePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\SitesFirstJourneyExperience.cs'
$sitesPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\Sites\SitesViewModel.cs'
$wizardPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\Sites\AddSiteWizardViewModel.cs'

foreach ($path in @($statePath, $experiencePath, $sitesPath, $wizardPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing Sites first journey contract file: $path"
    }
}

$state = Get-Content -LiteralPath $statePath -Raw
$experience = Get-Content -LiteralPath $experiencePath -Raw
$sites = Get-Content -LiteralPath $sitesPath -Raw
$wizard = Get-Content -LiteralPath $wizardPath -Raw

foreach ($token in @(
    'FirstJourneyRequirements',
    'FirstJourneyStatus',
    'IsFirstJourneyReady',
    'RefreshFirstJourneyReadiness',
    'Sites.Count > 0',
    'SelectedSite is not null',
    'SelectedSite?.IsConnected == true',
    'SelectedSiteDetails is not null',
    'Saved site',
    'Active selection',
    'Verified connection',
    'Local details loaded'
)) {
    if (-not $state.Contains($token)) {
        throw "Sites readiness state is missing contract token: $token"
    }
}

foreach ($token in @(
    'SitesFirstJourneyExperience',
    'STEP 1 · COMPLETE SITES SETUP',
    'Sites.FirstJourneyStatus',
    'Sites.FirstJourneyRequirements',
    'Sites.AddSiteCommand',
    'Sites.RetestSelectedSiteCommand',
    'Continue to WordPress Explorer',
    'CommandParameter = "WordPress Explorer"',
    'Sites.IsFirstJourneyReady',
    'FindButtonForCommand',
    'ReferenceEquals(button.Command, expectedCommand)',
    'Wizard.SiteSaved',
    'Sites.CollectionChanged'
)) {
    if (-not $experience.Contains($token)) {
        throw "Sites journey UI is missing contract token: $token"
    }
}

foreach ($token in @(
    'AddSiteCommand',
    'SelectSiteCommand',
    'RetestSelectedSiteCommand',
    'SelectedSiteDetails',
    '_currentSiteContext.SetCurrentSite',
    'UpdateConnectionResultAsync'
)) {
    if (-not $sites.Contains($token)) {
        throw "Sites page is missing required workflow contract: $token"
    }
}

foreach ($token in @(
    'Website information',
    'WordPress credentials',
    'Test and discover',
    'Review and start synchronization',
    'TestConnectionCommand',
    'IsConnectionSuccessful',
    'Save & start first sync',
    'SiteSaved'
)) {
    if (-not $wizard.Contains($token)) {
        throw "Add-site wizard is missing first journey contract: $token"
    }
}

Write-Host 'Sites page first journey readiness, wizard, selection, connection and navigation contracts validated successfully.' -ForegroundColor Green
