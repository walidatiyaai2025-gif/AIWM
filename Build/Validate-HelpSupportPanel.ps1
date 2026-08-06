[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$injectorPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\HelpSupportPanelInjector.cs'
$identityPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\BuildIdentityDisplay.cs'
$helpPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\HelpViewModel.cs'

foreach ($path in @($injectorPath, $identityPath, $helpPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing Help support panel contract file: $path"
    }
}

$injector = Get-Content -LiteralPath $injectorPath -Raw
$identity = Get-Content -LiteralPath $identityPath -Raw
$help = Get-Content -LiteralPath $helpPath -Raw

foreach ($token in @(
    'HelpSupportPanelInjector',
    'Support & Diagnostics',
    'Help.CreateSupportBundleCommand',
    'Help.VerifyLatestSupportBundleCommand',
    'Help.OpenLatestSupportBundleCommand',
    'Help.OpenSupportFolderCommand',
    'PrimaryButtonStyle',
    'SecondaryButtonStyle',
    'DispatcherPriority.Loaded',
    'ConditionalWeakTable<Window, object>',
    'InjectedWindows'
)) {
    if (-not $injector.Contains($token)) {
        throw "Help support panel is missing contract token: $token"
    }
}

if (-not $identity.Contains('HelpSupportPanelInjector.EnsureInjected(window)')) {
    throw 'Build identity startup path does not initialize the Help support panel.'
}

foreach ($token in @(
    'CreateSupportBundleCommand',
    'VerifyLatestSupportBundleCommand',
    'OpenLatestSupportBundleCommand',
    'OpenSupportFolderCommand'
)) {
    if (-not $help.Contains($token)) {
        throw "HelpViewModel is missing command required by the support panel: $token"
    }
}

Write-Host 'Help Support & Diagnostics panel contracts validated successfully.' -ForegroundColor Green
