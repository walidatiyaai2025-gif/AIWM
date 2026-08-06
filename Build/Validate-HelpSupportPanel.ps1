[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$injectorPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\HelpSupportPanelInjector.cs'
$identityPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\BuildIdentityDisplay.cs'
$helpPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\HelpViewModel.cs'
$supportSummaryPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\HelpViewModel.SupportSummary.cs'

foreach ($path in @($injectorPath, $identityPath, $helpPath, $supportSummaryPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing Help support panel contract file: $path"
    }
}

$injector = Get-Content -LiteralPath $injectorPath -Raw
$identity = Get-Content -LiteralPath $identityPath -Raw
$help = Get-Content -LiteralPath $helpPath -Raw
$supportSummary = Get-Content -LiteralPath $supportSummaryPath -Raw

foreach ($token in @(
    'HelpSupportPanelInjector',
    'Support & Diagnostics',
    'Help.CreateSupportBundleCommand',
    'Help.VerifyLatestSupportBundleCommand',
    'Help.CopySupportSummaryCommand',
    'Help.OpenLatestSupportBundleCommand',
    'Help.OpenSupportFolderCommand',
    'Help.SupportBundleVerificationStatus',
    'Help.LatestSupportBundlePath',
    'Copy support summary',
    'PrimaryButtonStyle',
    'SecondaryButtonStyle',
    'DispatcherPriority.Loaded',
    'ConditionalWeakTable<Window, object>',
    'InjectedWindows',
    'PendingWindows',
    'MaximumInjectionAttempts',
    'RetryDelay',
    'TryInjectAsync',
    'Task.Delay(RetryDelay)',
    'TextTrimming.CharacterEllipsis',
    'FindPanelContainingCommand',
    'viewModel.Help.OpenGuideCommand',
    'ReferenceEquals(button.Command, expectedCommand)',
    'FindPanelContainingHeading',
    'using System.Windows.Input',
    'using AIWordPressManager.Desktop.ViewModels'
)) {
    if (-not $injector.Contains($token)) {
        throw "Help support panel is missing contract token: $token"
    }
}

foreach ($token in @(
    '[RelayCommand]',
    'CopySupportSummary',
    'AI WordPress Manager Support Summary',
    'BuildIdentityDisplay.Version',
    'BuildIdentityDisplay.Branch',
    'BuildIdentityDisplay.FullCommit',
    'SupportBundleVerificationStatus',
    'SupportBundleBuildCompatibilityStatus',
    'LatestSupportBundlePath',
    'BuildIdentitySupportSnapshot.SnapshotPath',
    'Clipboard.SetText(summary)',
    'Support summary copied to the clipboard.'
)) {
    if (-not $supportSummary.Contains($token)) {
        throw "Copy support summary workflow is missing contract token: $token"
    }
}

if (-not $identity.Contains('HelpSupportPanelInjector.EnsureInjected(window)')) {
    throw 'Build identity startup path does not initialize the Help support panel.'
}

foreach ($token in @(
    'CreateSupportBundleCommand',
    'VerifyLatestSupportBundleCommand',
    'OpenLatestSupportBundleCommand',
    'OpenSupportFolderCommand',
    'SupportBundleVerificationStatus',
    'LatestSupportBundlePath'
)) {
    if (-not $help.Contains($token)) {
        throw "HelpViewModel is missing state or command required by the support panel: $token"
    }
}

Write-Host 'Localization-safe Help Support & Diagnostics panel, support summary, status, path, retries, and command contracts validated successfully.' -ForegroundColor Green
