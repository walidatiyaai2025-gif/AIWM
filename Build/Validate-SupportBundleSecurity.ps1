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
    'BuildIdentityDisplay.FullCommit'
)) {
    if (-not $bundle.Contains($token)) {
        throw "Support bundle security contract is missing token: $token"
    }
}

if ($bundle.Contains('source.CopyTo(target)')) {
    throw 'Support bundle logs must not be copied as raw bytes without redaction.'
}

Write-Host 'Support bundle redaction and manifest contracts validated successfully.' -ForegroundColor Green
