param(
    [string]$PhpExecutable = "php"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$zip = Join-Path $root "WordPressPlugins\AIWordPressManager-Bridge-1.3.0.zip"
$source = Join-Path $root "WordPressPlugins\ai-wordpress-manager-bridge\ai-wordpress-manager-bridge.php"

if (-not (Test-Path $zip)) { throw "Bridge ZIP not found: $zip" }
if (-not (Test-Path $source)) { throw "Bridge source not found: $source" }

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("aiwp-bridge-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    Expand-Archive -Path $zip -DestinationPath $temp -Force
    $zipPhp = Get-ChildItem -Path $temp -Filter *.php -Recurse | Select-Object -First 1
    if ($null -eq $zipPhp) { throw "The bridge ZIP does not contain a PHP entry file." }

    $sourceHash = (Get-FileHash -Algorithm SHA256 $source).Hash
    $zipHash = (Get-FileHash -Algorithm SHA256 $zipPhp.FullName).Hash
    if ($sourceHash -ne $zipHash) {
        throw "Bridge source and ZIP contents are different. Rebuild the ZIP before release."
    }

    $content = Get-Content $source -Raw
    foreach ($required in @(
        "Version: 1.3.0",
        "AIWP_MANAGER_BRIDGE_VERSION",
        "/health",
        "/visual-css",
        "/visual-css/rollback",
        "/visual-css/validate",
        "/visual-css/history",
        "/visual-css/history/rollback",
        "edit_theme_options"
    )) {
        if (-not $content.Contains($required)) {
            throw "Required bridge marker is missing: $required"
        }
    }

    $php = Get-Command $PhpExecutable -ErrorAction SilentlyContinue
    if ($null -ne $php) {
        & $php.Source -l $source
        if ($LASTEXITCODE -ne 0) { throw "PHP syntax validation failed." }
    }
    else {
        Write-Warning "PHP executable was not found; syntax validation was skipped."
    }

    Write-Host "Bridge validation passed." -ForegroundColor Green
    Write-Host "Source/ZIP SHA256: $sourceHash"
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
