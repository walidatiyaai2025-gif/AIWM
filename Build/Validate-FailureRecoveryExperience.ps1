[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root 'src/AIWordPressManager.Desktop/Services/FailureRecoveryDialog.cs'

if (-not (Test-Path $path)) {
    throw "Failure recovery dialog not found: $path"
}

$content = Get-Content -Raw -Path $path

$requiredTokens = @(
    'FailureRecoveryRequest',
    'FailureRecoveryDecision',
    'RetryAsync',
    'SkipAsync',
    'PauseAsync',
    'RollbackAsync',
    'Copy details',
    'Copy solution',
    'Open evidence',
    'TaskCompletionSource<FailureRecoveryDecision>',
    'CreateLinkedTokenSource',
    'linkedCancellation.Dispose()',
    'SetButtonsEnabled(actionButtons, false)',
    'Recovery action failed:',
    'Could not copy:'
)

foreach ($token in $requiredTokens) {
    if (-not $content.Contains($token, [StringComparison]::Ordinal)) {
        throw "Failure recovery contract is missing token: $token"
    }
}

$forbiddenTokens = @(
    'new Action(async',
    'button.Click += async',
    'using var linkedCancellation'
)

foreach ($token in $forbiddenTokens) {
    if ($content.Contains($token, [StringComparison]::Ordinal)) {
        throw "Unsafe failure recovery pattern detected: $token"
    }
}

Write-Host 'Failure recovery experience contract passed.' -ForegroundColor Green
