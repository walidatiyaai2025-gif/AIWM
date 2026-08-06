[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$receiptPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\ExecutionCenterViewModel.Receipts.cs'
$dashboardPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\MainWindowViewModel.ExecutionReceipts.cs'
$appPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\App.GuidedTour.cs'
$buildRunPath = Join-Path $repoRoot 'Build-And-Run.bat'

foreach ($path in @($receiptPath, $dashboardPath, $appPath, $buildRunPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing execution receipt contract file: $path"
    }
}

$receipt = Get-Content -LiteralPath $receiptPath -Raw
$dashboard = Get-Content -LiteralPath $dashboardPath -Raw
$app = Get-Content -LiteralPath $appPath -Raw
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

if (-not $app.Contains('viewModel.BindExecutionReceiptStore()')) {
    throw 'The dashboard receipt binding is not initialized after the workspace becomes active.'
}

if (-not $buildRun.Contains('TARGET_BRANCH=feature/execution-receipts-and-audit-trail')) {
    throw 'Build-And-Run.bat is not pointing to the active development branch.'
}

Write-Host 'Persistent execution receipts and dashboard audit-trail integration validated successfully.' -ForegroundColor Green
