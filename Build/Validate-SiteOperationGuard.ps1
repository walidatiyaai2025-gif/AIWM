[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$guardPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\Services\Sites\SiteOperationGuard.cs'
$suggestedPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\SuggestedChangesViewModel.cs'
$appPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\App.xaml.cs'

foreach ($path in @($guardPath, $suggestedPath, $appPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing site-operation safety file: $path"
    }
}

$guard = Get-Content -LiteralPath $guardPath -Raw
$suggested = Get-Content -LiteralPath $suggestedPath -Raw
$app = Get-Content -LiteralPath $appPath -Raw

foreach ($token in @(
    'SiteOperationLease',
    'SiteOperationLease Begin(string operationName)',
    'bool IsCurrent(SiteOperationLease lease)',
    'void EnsureCurrent(SiteOperationLease lease)',
    'OperationCanceledException',
    'No further WordPress action was allowed'
)) {
    if (-not $guard.Contains($token)) {
        throw "SiteOperationGuard is missing contract token: $token"
    }
}

foreach ($token in @(
    'ISiteOperationGuard',
    '_siteOperationGuard.Begin(',
    '_siteOperationGuard.EnsureCurrent(',
    '_siteOperationGuard.IsCurrent(',
    'Applying an AI suggestion',
    'Applying selected safe suggestions',
    'Generating suggested changes',
    'Marking selected proposals as',
    'ExecuteAutomaticallyAsync(SiteOperationLease lease',
    'CaptureEvidenceAsync(string siteUrl, string stage, SiteOperationLease lease)'
)) {
    if (-not $suggested.Contains($token)) {
        throw "SuggestedChangesViewModel is missing site-bound safety token: $token"
    }
}

if ($suggested.Contains('_executionService.ExecuteAsync(site.Id')) {
    throw 'SuggestedChangesViewModel still executes against a mutable SitesViewModel site reference.'
}

if (-not $app.Contains('ISiteOperationGuard, AIWordPressManager.Desktop.Services.Sites.SiteOperationGuard')) {
    throw 'SiteOperationGuard is not registered in desktop dependency injection.'
}

Write-Host 'Site-bound proposal approval and execution workflow validated successfully.' -ForegroundColor Green
