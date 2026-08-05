[CmdletBinding()]
param(
    [AllowNull()]
    [AllowEmptyString()]
    [string]$ProjectPath,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$SkipClean,
    [switch]$NoRun,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-Root {
    param([AllowNull()][AllowEmptyString()][string]$RequestedPath)

    foreach ($candidate in @($RequestedPath, $PSScriptRoot, (Get-Location).ProviderPath)) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        $clean = $candidate.Trim().Replace('"', '').Replace("'", '')
        if ([string]::IsNullOrWhiteSpace($clean)) { continue }
        try { $full = [System.IO.Path]::GetFullPath($clean) } catch { continue }
        if (Test-Path -LiteralPath $full -PathType Container) { return $full }
    }

    throw "Could not resolve the repository root."
}

function Wait-ForUser {
    if ($NoPause) { return }
    Write-Host ""
    Write-Host "==============================================" -ForegroundColor DarkCyan
    Write-Host "Press ENTER to close this window..." -ForegroundColor Magenta
    Write-Host "==============================================" -ForegroundColor DarkCyan
    try { [void](Read-Host) }
    catch {
        try { [void]$Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") }
        catch { Start-Sleep -Seconds 15 }
    }
}

$exitCode = 0
$root = $null
$latest = $null
$log = $null

try {
    $root = Resolve-Root -RequestedPath $ProjectPath
    Set-Location -LiteralPath $root

    $latest = Join-Path $root "Output\Latest"
    New-Item -ItemType Directory -LiteralPath $latest -Force | Out-Null
    $log = Join-Path $latest "preflight-and-update.log"
    Set-Content -LiteralPath $log -Value "Started: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding UTF8

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw "git is not available in PATH." }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "dotnet is not available in PATH." }

    Write-Host "" 
    Write-Host "==> Fetching the latest safe launchers" -ForegroundColor Cyan
    & git fetch origin 2>&1 | Tee-Object -LiteralPath $log -Append | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "git fetch failed." }

    foreach ($launcher in @(
        "Update-And-Run.ps1",
        "Validate-Root-PowerShell-Paths.ps1",
        "Run-Preflight-And-Update.ps1"
    )) {
        & git checkout origin/main -- $launcher 2>&1 | Tee-Object -LiteralPath $log -Append | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Could not refresh $launcher from origin/main." }
    }

    $buildHelper = Join-Path $root "Build\RootScript.Common.ps1"
    if (Test-Path -LiteralPath (Join-Path $root "Build") -PathType Container) {
        & git checkout origin/main -- "Build/RootScript.Common.ps1" 2>&1 |
            Tee-Object -LiteralPath $log -Append | Out-Host
    }

    Write-Host ""
    Write-Host "==> Validating root PowerShell launchers" -ForegroundColor Cyan
    $validator = Join-Path $root "Validate-Root-PowerShell-Paths.ps1"
    & powershell.exe -ExecutionPolicy Bypass -File $validator -ProjectPath $root -NoPause 2>&1 |
        Tee-Object -LiteralPath $log -Append | Out-Host
    $validationCode = $LASTEXITCODE

    if ($validationCode -eq 1) {
        throw "Root PowerShell validation failed with a fatal error."
    }
    elseif ($validationCode -eq 2) {
        Write-Host "Validation completed with review notes; build will continue." -ForegroundColor Yellow
    }
    else {
        Write-Host "Root PowerShell validation passed." -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "==> Starting update, restore, build, and run" -ForegroundColor Cyan
    $runner = Join-Path $root "Update-And-Run.ps1"
    $arguments = @(
        "-ExecutionPolicy", "Bypass",
        "-File", $runner,
        "-ProjectPath", $root,
        "-Configuration", $Configuration,
        "-NoPause"
    )
    if ($SkipClean) { $arguments += "-SkipClean" }
    if ($NoRun) { $arguments += "-NoRun" }

    & powershell.exe @arguments 2>&1 | Tee-Object -LiteralPath $log -Append | Out-Host
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { throw "Update or build failed with exit code $exitCode." }

    Add-Content -LiteralPath $log -Value "Completed successfully: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding UTF8
    Write-Host ""
    Write-Host "Preflight, update, and build completed successfully." -ForegroundColor Green
}
catch {
    $exitCode = if ($exitCode -ne 0) { $exitCode } else { 1 }
    $message = "[ERROR] $($_.Exception.Message)"
    Write-Host ""
    Write-Host $message -ForegroundColor Red
    if (-not [string]::IsNullOrWhiteSpace($log)) {
        try { Add-Content -LiteralPath $log -Value $message -Encoding UTF8 } catch { }
    }
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($latest)) {
        try { Start-Process explorer.exe -ArgumentList $latest | Out-Null } catch { }
    }
    Wait-ForUser
}

exit $exitCode
