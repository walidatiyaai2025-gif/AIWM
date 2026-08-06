[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$bundlePath = Join-Path $repoRoot 'src\AIWordPressManager.Desktop\SupportBundleService.cs'

if (-not (Test-Path -LiteralPath $bundlePath)) {
    throw "Missing support bundle service: $bundlePath"
}

$bundle = Get-Content -LiteralPath $bundlePath -Raw

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
    'Convert.ToHexString',
    'Included entries and SHA-256',
    'Integrity: SHA-256 is recorded',
    'IDictionary<string, string> entryHashes'
)) {
    if (-not $bundle.Contains($token)) {
        throw "Support bundle security, retention, or integrity contract is missing token: $token"
    }
}

if ($bundle.Contains('source.CopyTo(target)')) {
    throw 'Support bundle logs must not be copied as raw bytes without redaction.'
}

Write-Host 'Support bundle redaction, manifest, retention, and SHA-256 integrity contracts validated successfully.' -ForegroundColor Green
