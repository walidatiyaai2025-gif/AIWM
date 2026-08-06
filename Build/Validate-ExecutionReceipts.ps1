[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$receiptPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\ExecutionCenterViewModel.Receipts.cs'
$dashboardPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\MainWindowViewModel.ExecutionReceipts.cs'
$appPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\App.GuidedTour.cs'
$identityPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\BuildIdentityDisplay.cs'
$projectPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\AIWordPressManager.Desktop.csproj'
$buildRunPath = Join-Path $repoRoot 'Build-And-Run.bat'

foreach ($path in @($receiptPath, $dashboardPath, $appPath, $identityPath, $projectPath, $buildRunPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing execution receipt or build identity contract file: $path"
    }
}

$receipt = Get-Content -LiteralPath $receiptPath -Raw
$dashboard = Get-Content -LiteralPath $dashboardPath -Raw
$app = Get-Content -LiteralPath $appPath -Raw
$identity = Get-Content -LiteralPath $identityPath -Raw
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
    'BuildIdentityDisplay.Apply(mainWindow)',
    'Version {Version} • Branch {Branch}',
    'DiagnosticText',
    'Clipboard.SetText(DiagnosticText)',
    'MouseLeftButtonUp += CopyBuildIdentityToClipboard',
    'Cursors.Hand',
    'Click to copy complete build information.',
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
    '<Version>2.2.7</Version>',
    '<AssemblyVersion>2.2.7.0</AssemblyVersion>',
    '<FileVersion>2.2.7.0</FileVersion>',
    '<InformationalVersion>2.2.7</InformationalVersion>',
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

Write-Host 'Execution receipts and copyable version/branch/commit identity contracts validated successfully.' -ForegroundColor Green
