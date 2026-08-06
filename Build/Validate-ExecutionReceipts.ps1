[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$receiptPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\ExecutionCenterViewModel.Receipts.cs'
$dashboardPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\MainWindowViewModel.ExecutionReceipts.cs'
$helpPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\HelpViewModel.cs'
$appPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\App.GuidedTour.cs'
$identityPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\BuildIdentityDisplay.cs'
$diagnosticsPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\BuildIdentityDiagnostics.cs'
$snapshotPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\BuildIdentitySupportSnapshot.cs'
$bundlePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\SupportBundleService.cs'
$projectPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\AIWordPressManager.Desktop.csproj'
$buildRunPath = Join-Path $repoRoot 'Build-And-Run.bat'

foreach ($path in @($receiptPath, $dashboardPath, $helpPath, $appPath, $identityPath, $diagnosticsPath, $snapshotPath, $bundlePath, $projectPath, $buildRunPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing execution receipt or build identity contract file: $path"
    }
}

$receipt = Get-Content -LiteralPath $receiptPath -Raw
$dashboard = Get-Content -LiteralPath $dashboardPath -Raw
$help = Get-Content -LiteralPath $helpPath -Raw
$app = Get-Content -LiteralPath $appPath -Raw
$identity = Get-Content -LiteralPath $identityPath -Raw
$diagnostics = Get-Content -LiteralPath $diagnosticsPath -Raw
$snapshot = Get-Content -LiteralPath $snapshotPath -Raw
$bundle = Get-Content -LiteralPath $bundlePath -Raw
$project = Get-Content -LiteralPath $projectPath -Raw
$buildRun = Get-Content -LiteralPath $buildRunPath -Raw

foreach ($token in @(
    'ExecutionReceiptDocument',
    'partial void OnQueueStateChanged',
    'WriteExecutionReceiptSafeAsync',
    'ExecutionReceipt_',
    'Receipts',
    'BeforeEvidencePath',
    'AfterEvidencePath',
    'ApplicationVersion',
    'SourceBranch: BuildIdentityDisplay.Branch',
    'SourceCommit: BuildIdentityDisplay.Commit',
    '<th>Source branch</th>',
    '<th>Source commit</th>',
    'OpenLatestReceiptCommand',
    'OpenReceiptsFolderCommand',
    'latest-receipt.txt',
    'FindLatestReceiptPath',
    'ResolveLatestReceiptPath',
    'Completed with failures',
    'JsonSerializer.Serialize',
    'BuildReceiptHtml'
)) {
    if (-not $receipt.Contains($token)) {
        throw "Execution receipt implementation is missing contract token: $token"
    }
}

foreach ($token in @(
    'BindExecutionReceiptStore',
    'ExecutionCenter.PropertyChanged',
    'LatestReceiptPath',
    'LatestReceiptStatus',
    'LastOptimizationReceiptPath',
    'OpenLastOptimizationReceiptCommand.NotifyCanExecuteChanged'
)) {
    if (-not $dashboard.Contains($token)) {
        throw "Dashboard receipt integration is missing contract token: $token"
    }
}

foreach ($token in @(
    'CreateSupportBundleCommand',
    'OpenSupportFolderCommand',
    'OpenLatestSupportBundleCommand',
    'VerifyLatestSupportBundleCommand',
    'SupportBundleVerificationStatus',
    'LatestSupportBundlePath',
    'SupportBundleService.CreateBundle()',
    'SupportBundleService.VerifyBundle',
    'FindLatestSupportBundlePath',
    'Ctrl + Click version',
    'Ctrl + Shift + B',
    'Ctrl + Shift + I'
)) {
    if (-not $help.Contains($token)) {
        throw "Help support workflow is missing contract token: $token"
    }
}

foreach ($token in @(
    'BuildIdentityDisplay.Apply(mainWindow)',
    'BuildIdentityDiagnostics.LogOnce()',
    'Version {Version} • Branch {Branch}',
    'DiagnosticText',
    'BuildIdentitySupportSnapshot.WriteOnce()',
    'BuildIdentitySupportSnapshot.SnapshotPath',
    'Clipboard.SetText(DiagnosticText)',
    'MouseLeftButtonUp += CopyBuildIdentityToClipboard',
    'CreateSupportContextMenu()',
    'Create support bundle ZIP',
    'Verify latest support bundle',
    'VerifyLatestSupportBundle',
    'Open support bundles folder',
    'Open support snapshot',
    'SupportBundleService.CreateBundle()',
    'SupportBundleService.VerifyBundle',
    'ModifierKeys.Control',
    'BindGlobalSupportShortcuts(window)',
    'CreateSupportBundleShortcutCommand',
    'CopyBuildIdentityShortcutCommand',
    'Key.B',
    'Key.I',
    'ModifierKeys.Control | ModifierKeys.Shift',
    'BoundWindows',
    'Cursors.Hand',
    'Right-click for support actions.',
    'AssemblyMetadataAttribute',
    'SourceBranch',
    'SourceCommit',
    'AI WordPress Website Manager • Offline-first'
)) {
    if (-not ($app + $identity).Contains($token)) {
        throw "Build identity display is missing contract token: $token"
    }
}

foreach ($token in @(
    'BuildIdentityDisplay.Version',
    'BuildIdentityDisplay.Branch',
    'BuildIdentityDisplay.Commit',
    'Interlocked.Exchange',
    'Application build identity'
)) {
    if (-not $diagnostics.Contains($token)) {
        throw "Build identity diagnostics logging is missing contract token: $token"
    }
}

foreach ($token in @(
    'support-snapshot.txt',
    'RuntimeInformation.OSDescription',
    'RuntimeInformation.FrameworkDescription',
    'BuildIdentityDisplay.FullCommit',
    'Working set MB',
    'Base directory',
    'File.WriteAllText',
    'Interlocked.Exchange'
)) {
    if (-not $snapshot.Contains($token)) {
        throw "Persistent support snapshot is missing contract token: $token"
    }
}

foreach ($token in @(
    'SupportBundles',
    'ZipFile.Open',
    'build-identity.txt',
    'support-snapshot.txt',
    'startup-history.log',
    'application-*.log',
    'ExecutionReceipt_*.*',
    'FileShare.ReadWrite',
    'BuildIdentityDisplay.Commit',
    'VerifyBundle(string bundlePath)',
    'SupportBundleVerificationResult'
)) {
    if (-not $bundle.Contains($token)) {
        throw "Diagnostic support bundle is missing contract token: $token"
    }
}

foreach ($token in @(
    '<Version>2.2.9</Version>',
    '<AssemblyVersion>2.2.9.0</AssemblyVersion>',
    '<FileVersion>2.2.9.0</FileVersion>',
    '<InformationalVersion>2.2.9</InformationalVersion>',
    '<AssemblyMetadata Include="SourceBranch" Value="$(SourceBranchName)" />',
    '<AssemblyMetadata Include="SourceCommit" Value="$(SourceCommitSha)" />',
    '<SourceBranchName Condition=',
    '<SourceCommitSha Condition=',
    'GITHUB_HEAD_REF',
    'GITHUB_REF_NAME',
    'GITHUB_SHA'
)) {
    if (-not $project.Contains($token)) {
        throw "Desktop project build metadata or version is missing contract token: $token"
    }
}

if (-not $app.Contains('viewModel.BindExecutionReceiptStore()')) {
    throw 'The dashboard receipt binding is not initialized after the workspace becomes active.'
}

if (-not $buildRun.Contains('TARGET_BRANCH=feature/execution-receipts-and-audit-trail')) {
    throw 'Build-And-Run.bat is not pointing to the active development branch.'
}

foreach ($token in @(
    'git rev-parse HEAD',
    '/p:SourceBranchName="%TARGET_BRANCH%"',
    '/p:SourceCommitSha="%SOURCE_COMMIT%"'
)) {
    if (-not $buildRun.Contains($token)) {
        throw "Build-And-Run.bat does not embed the complete build identity token: $token"
    }
}

Write-Host 'Execution receipts, version 2.2.9 build identity, global support shortcuts, support bundle verification, and diagnostics contracts validated successfully.' -ForegroundColor Green
