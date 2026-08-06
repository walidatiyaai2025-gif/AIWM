[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$paths = @{
    Receipt = 'src\AIWordPressManager.Desktop\ViewModels\ExecutionCenterViewModel.Receipts.cs'
    Dashboard = 'src\AIWordPressManager.Desktop\ViewModels\MainWindowViewModel.ExecutionReceipts.cs'
    Help = 'src\AIWordPressManager.Desktop\ViewModels\HelpViewModel.cs'
    App = 'src\AIWordPressManager.Desktop\App.GuidedTour.cs'
    Identity = 'src\AIWordPressManager.Desktop\BuildIdentityDisplay.cs'
    Diagnostics = 'src\AIWordPressManager.Desktop\BuildIdentityDiagnostics.cs'
    Snapshot = 'src\AIWordPressManager.Desktop\BuildIdentitySupportSnapshot.cs'
    Bundle = 'src\AIWordPressManager.Desktop\SupportBundleService.cs'
    Project = 'src\AIWordPressManager.Desktop\AIWordPressManager.Desktop.csproj'
    BuildRun = 'Build-And-Run.bat'
}

$content = @{}
foreach ($key in $paths.Keys) {
    $path = Join-Path $repoRoot $paths[$key]
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing execution receipt contract file: $path" }
    $content[$key] = Get-Content -LiteralPath $path -Raw
}

function Assert-Tokens([string]$Name, [string]$Text, [string[]]$Tokens) {
    foreach ($token in $Tokens) {
        if (-not $Text.Contains($token)) { throw "$Name is missing contract token: $token" }
    }
}

Assert-Tokens 'Execution receipt implementation' $content.Receipt @(
    'ExecutionReceiptDocument', 'WriteExecutionReceiptSafeAsync', 'ExecutionReceipt_',
    'ApplicationVersion', 'SourceBranch: BuildIdentityDisplay.Branch',
    'SourceCommit: BuildIdentityDisplay.Commit', 'OpenLatestReceiptCommand',
    'OpenReceiptsFolderCommand', 'latest-receipt.txt', 'JsonSerializer.Serialize', 'BuildReceiptHtml'
)

Assert-Tokens 'Dashboard receipt integration' $content.Dashboard @(
    'BindExecutionReceiptStore', 'ExecutionCenter.PropertyChanged', 'LatestReceiptPath',
    'LatestReceiptStatus', 'LastOptimizationReceiptPath'
)

Assert-Tokens 'Help support workflow' $content.Help @(
    'CreateSupportBundleCommand', 'OpenSupportFolderCommand', 'OpenLatestSupportBundleCommand',
    'VerifyLatestSupportBundleCommand', 'SupportBundleVerificationStatus', 'LatestSupportBundlePath'
)

Assert-Tokens 'Build identity display' ($content.App + $content.Identity) @(
    'BuildIdentityDisplay.Apply(mainWindow)', 'BuildIdentityDiagnostics.LogOnce()',
    'Version {Version} • Branch {Branch}', 'DiagnosticText', 'Clipboard.SetText(DiagnosticText)',
    'CreateSupportContextMenu()', 'Create support bundle ZIP', 'Verify latest support bundle',
    'BindGlobalSupportShortcuts(window)', 'SourceBranch', 'SourceCommit'
)

Assert-Tokens 'Build identity diagnostics' $content.Diagnostics @(
    'BuildIdentityDisplay.Version', 'BuildIdentityDisplay.Branch', 'BuildIdentityDisplay.Commit',
    'Interlocked.Exchange', 'Application build identity'
)

Assert-Tokens 'Support snapshot' $content.Snapshot @(
    'support-snapshot.txt', 'RuntimeInformation.OSDescription',
    'RuntimeInformation.FrameworkDescription', 'BuildIdentityDisplay.FullCommit', 'File.WriteAllText'
)

Assert-Tokens 'Support bundle' $content.Bundle @(
    'SupportBundles', 'ZipFile.Open', 'build-identity.txt', 'support-snapshot.txt',
    'application-*.log', 'ExecutionReceipt_*.*', 'VerifyBundle(string bundlePath)',
    'SupportBundleVerificationResult'
)

Assert-Tokens 'Desktop project version and build metadata' $content.Project @(
    '<Version>2.3.0</Version>', '<AssemblyVersion>2.3.0.0</AssemblyVersion>',
    '<FileVersion>2.3.0.0</FileVersion>', '<InformationalVersion>2.3.0</InformationalVersion>',
    '<AssemblyMetadata Include="SourceBranch" Value="$(SourceBranchName)" />',
    '<AssemblyMetadata Include="SourceCommit" Value="$(SourceCommitSha)" />',
    'GITHUB_HEAD_REF', 'GITHUB_REF_NAME', 'GITHUB_SHA',
    '<CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>'
)

Assert-Tokens 'Build-And-Run build identity' $content.BuildRun @(
    'TARGET_BRANCH=feature/execution-receipts-and-audit-trail',
    'git rev-parse HEAD', '/p:SourceBranchName="%TARGET_BRANCH%"',
    '/p:SourceCommitSha="%SOURCE_COMMIT%"'
)

if (-not $content.App.Contains('viewModel.BindExecutionReceiptStore()')) {
    throw 'The dashboard receipt binding is not initialized after workspace activation.'
}

Write-Host 'Execution receipts, version 2.3.0 build identity, support bundle, and diagnostics contracts validated successfully.' -ForegroundColor Green
