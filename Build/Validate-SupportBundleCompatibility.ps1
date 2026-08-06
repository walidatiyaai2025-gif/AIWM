[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$compatibilityPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\SupportBundleBuildCompatibility.cs'
$helpPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\HelpViewModel.cs'

foreach ($path in @($compatibilityPath, $helpPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing support bundle compatibility contract file: $path"
    }
}

$compatibility = Get-Content -LiteralPath $compatibilityPath -Raw
$help = Get-Content -LiteralPath $helpPath -Raw

foreach ($token in @(
    'ZipFile.OpenRead',
    'manifest.txt',
    'ReadManifestValue',
    'Version:',
    'Branch:',
    'Commit:',
    'BuildIdentityDisplay.Version',
    'BuildIdentityDisplay.Branch',
    'BuildIdentityDisplay.FullCommit',
    'IsCurrentBuild',
    'VersionMatches',
    'BranchMatches',
    'CommitMatches',
    'belongs to a different application build'
)) {
    if (-not $compatibility.Contains($token)) {
        throw "Support bundle build compatibility contract is missing token: $token"
    }
}

foreach ($token in @(
    'SupportBundleBuildCompatibilityStatus',
    'SupportBundleBuildCompatibility.Inspect',
    'FormatCompatibilityStatus',
    'Build compatibility verified',
    'Bundle belongs to a different build'
)) {
    if (-not $help.Contains($token)) {
        throw "Help build compatibility workflow is missing token: $token"
    }
}

Write-Host 'Support bundle version, branch, and commit compatibility contracts validated successfully.' -ForegroundColor Green
