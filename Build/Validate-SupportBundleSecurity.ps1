[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$bundlePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\SupportBundleService.cs'
$helpPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\ViewModels\HelpViewModel.cs'
$identityPath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\BuildIdentityDisplay.cs'

foreach ($path in @($bundlePath, $helpPath, $identityPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing support bundle contract file: $path"
    }
}

$bundle = Get-Content -LiteralPath $bundlePath -Raw
$help = Get-Content -LiteralPath $helpPath -Raw
$identity = Get-Content -LiteralPath $identityPath -Raw

foreach ($token in @(
    'SensitiveAssignmentPattern',
    'BearerTokenPattern',
    'JsonSecretPattern',
    '<REDACTED>',
    'AddSanitizedFileIfExists',
    'manifest.txt',
    'Sensitive values are automatically replaced',
    'BuildManifest',
    'FileShare.ReadWrite',
    'BuildIdentityDisplay.FullCommit',
    'MaximumRetainedBundles = 10',
    'DeleteExpiredBundles(bundlePath)',
    'Skip(MaximumRetainedBundles - 1)',
    'Retention: latest {MaximumRetainedBundles} support bundles',
    'using System.Security.Cryptography',
    'SHA256.HashData(bytes)',
    'SHA256.HashData(stream)',
    'Convert.ToHexString',
    'Included entries and SHA-256',
    'Integrity: SHA-256 is recorded',
    'IDictionary<string, string> entryHashes',
    'VerifyBundle(string bundlePath)',
    'ParseManifestHashes',
    'Hash mismatch:',
    'Missing entry:',
    'SupportBundleVerificationResult'
)) {
    if (-not $bundle.Contains($token)) {
        throw "Support bundle security, retention, or integrity contract is missing token: $token"
    }
}

foreach ($token in @(
    'VerifyLatestSupportBundleCommand',
    'VerifyLatestSupportBundleAsync',
    'SupportBundleVerificationStatus',
    'FormatVerificationStatus',
    'SupportBundleService.VerifyBundle',
    'Integrity verified:'
)) {
    if (-not $help.Contains($token)) {
        throw "Help support bundle verification is missing token: $token"
    }
}

foreach ($token in @(
    'Verify latest support bundle',
    'VerifyLatestSupportBundle',
    'SupportBundleService.VerifyBundle',
    'All recorded SHA-256 values match.'
)) {
    if (-not $identity.Contains($token)) {
        throw "Footer support bundle verification is missing token: $token"
    }
}

if ($bundle.Contains('source.CopyTo(target)')) {
    throw 'Support bundle logs must not be copied as raw bytes without redaction.'
}

Write-Host 'Support bundle redaction, manifest, retention, SHA-256 creation, and verification contracts validated successfully.' -ForegroundColor Green
