[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$contractPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\Services\Sites\ICurrentSiteContext.cs'
$implementationPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\Services\Sites\CurrentSiteContext.cs'
$sitesViewModelPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\Sites\SitesViewModel.cs'

foreach ($path in @($contractPath, $implementationPath, $sitesViewModelPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing current-site context file: $path"
    }
}

$contract = Get-Content -LiteralPath $contractPath -Raw
$implementation = Get-Content -LiteralPath $implementationPath -Raw
$sitesViewModel = Get-Content -LiteralPath $sitesViewModelPath -Raw

$requiredContractTokens = @(
    'CurrentSiteSnapshot',
    'CurrentSiteChangedEventArgs',
    'long Version',
    'CurrentSiteSnapshot Snapshot',
    'CurrentSiteSnapshot Capture()',
    'bool IsCurrent(CurrentSiteSnapshot snapshot)',
    'void ClearCurrentSite()'
)

foreach ($token in $requiredContractTokens) {
    if (-not $contract.Contains($token)) {
        throw "Current-site contract is missing: $token"
    }
}

$requiredImplementationTokens = @(
    'private readonly object _gate',
    'lock (_gate)',
    'checked(previous.Version + 1)',
    'DateTime.UtcNow',
    'CurrentSiteChanged?.Invoke',
    'new CurrentSiteChangedEventArgs(previous, current)'
)

foreach ($token in $requiredImplementationTokens) {
    if (-not $implementation.Contains($token)) {
        throw "Current-site implementation is missing: $token"
    }
}

if (-not $sitesViewModel.Contains('_currentSiteContext.SetCurrentSite')) {
    throw 'SitesViewModel does not publish the selected site to the shared context.'
}

Write-Host 'Unified current-site context contracts validated successfully.' -ForegroundColor Green
