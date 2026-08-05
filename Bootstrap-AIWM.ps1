[CmdletBinding()]
param(
    [string]$RepositoryUrl = "https://github.com/walidatiyaai2025-gif/AIWM.git",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$NoRun,
    [switch]$SkipPrerequisiteInstall,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$exitCode = 0
$projectRoot = $null
$latestOutput = $null
$historyOutput = $null
$logPath = $null
$historyLogPath = $null

function Wait-BeforeExit {
    if ($NoPause) { return }
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor DarkCyan
    Write-Host "Press ENTER to close this window..." -ForegroundColor Magenta
    Write-Host "============================================================" -ForegroundColor DarkCyan
    try { [void](Read-Host) }
    catch {
        try { [void]$Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") }
        catch { Start-Sleep -Seconds 20 }
    }
}

function Resolve-SafePath {
    param([Parameter(Mandatory)][string]$Path)
    $clean = $Path.Trim().Replace('"', '').Replace("'", '')
    if ([string]::IsNullOrWhiteSpace($clean)) { throw "The working directory path is empty." }
    return [System.IO.Path]::GetFullPath($clean)
}

function Ensure-Directory {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Initialize-Output {
    param([Parameter(Mandatory)][string]$Root)
    $outputRoot = Join-Path $Root "Output"
    $script:latestOutput = Join-Path $outputRoot "Latest"
    $historyRoot = Join-Path $outputRoot "History"
    $script:historyOutput = Join-Path $historyRoot (Get-Date -Format "yyyy-MM-dd_HH-mm-ss")
    foreach ($folder in @($outputRoot, $script:latestOutput, $historyRoot, $script:historyOutput)) {
        Ensure-Directory -Path $folder
    }
    $script:logPath = Join-Path $script:latestOutput "bootstrap-aiwm.log"
    $script:historyLogPath = Join-Path $script:historyOutput "bootstrap-aiwm.log"
    $start = "AIWM bootstrap started at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Set-Content -LiteralPath $script:logPath -Value $start -Encoding UTF8
    Set-Content -LiteralPath $script:historyLogPath -Value $start -Encoding UTF8
}

function Write-Log {
    param([Parameter(Mandatory)][string]$Message)
    $line = "[$(Get-Date -Format 'HH:mm:ss')] $Message"
    Write-Host $line
    if ($script:logPath) { Add-Content -LiteralPath $script:logPath -Value $line -Encoding UTF8 }
    if ($script:historyLogPath) { Add-Content -LiteralPath $script:historyLogPath -Value $line -Encoding UTF8 }
}

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
    Write-Log -Message $Message
}

function Test-CommandAvailable {
    param([Parameter(Mandatory)][string]$Name)
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Refresh-PathEnvironment {
    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = "$machinePath;$userPath"
}

function Install-WingetPackage {
    param([string]$Id, [string]$DisplayName)
    if ($SkipPrerequisiteInstall) { throw "$DisplayName is missing and automatic installation was skipped." }
    if (-not (Test-CommandAvailable -Name "winget")) {
        throw "winget is unavailable. Install Microsoft App Installer, then rerun this script."
    }
    Write-Step "Installing $DisplayName"
    & winget install --id $Id --exact --silent --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -ne 0) { throw "Failed to install $DisplayName. Exit code: $LASTEXITCODE" }
    Refresh-PathEnvironment
}

function Ensure-Prerequisites {
    Write-Step "Checking prerequisites"
    if (-not (Test-CommandAvailable -Name "git")) {
        Install-WingetPackage -Id "Git.Git" -DisplayName "Git for Windows"
    }
    if (-not (Test-CommandAvailable -Name "dotnet")) {
        Install-WingetPackage -Id "Microsoft.DotNet.SDK.8" -DisplayName ".NET 8 SDK"
    }
    Refresh-PathEnvironment
    if (-not (Test-CommandAvailable -Name "git")) { throw "Git is not available." }
    if (-not (Test-CommandAvailable -Name "dotnet")) { throw ".NET is not available." }
    $sdks = & dotnet --list-sdks
    if ($LASTEXITCODE -ne 0 -or -not ($sdks -match '^8\.')) {
        Install-WingetPackage -Id "Microsoft.DotNet.SDK.8" -DisplayName ".NET 8 SDK"
        $sdks = & dotnet --list-sdks
        if (-not ($sdks -match '^8\.')) { throw ".NET 8 SDK was not detected after installation." }
    }
    Write-Log -Message "Git: $((& git --version) -join ' ')"
    Write-Log -Message ".NET SDKs: $($sdks -join '; ')"
}

function Invoke-LoggedCommand {
    param([string]$Command, [string[]]$Arguments = @(), [switch]$IgnoreExitCode)
    Write-Log -Message ("RUN: {0} {1}" -f $Command, ($Arguments -join ' '))
    $output = & $Command @Arguments 2>&1
    $code = $LASTEXITCODE
    $output | Out-Host
    if ($null -ne $output) {
        $output | Add-Content -LiteralPath $script:logPath -Encoding UTF8
        $output | Add-Content -LiteralPath $script:historyLogPath -Encoding UTF8
    }
    if (-not $IgnoreExitCode -and $code -ne 0) { throw "$Command failed with exit code $code." }
    return $code
}

function Prepare-Repository {
    $gitFolder = Join-Path $script:projectRoot ".git"
    if (Test-Path -LiteralPath $gitFolder -PathType Container) {
        Set-Location -LiteralPath $script:projectRoot
        Write-Step "Updating existing repository in the current folder"
        Invoke-LoggedCommand -Command "git" -Arguments @("fetch", "origin", "--prune") | Out-Null
        Invoke-LoggedCommand -Command "git" -Arguments @("checkout", "main") | Out-Null
        Invoke-LoggedCommand -Command "git" -Arguments @("reset", "--hard", "origin/main") | Out-Null
        Invoke-LoggedCommand -Command "git" -Arguments @("clean", "-fd") | Out-Null
        return
    }

    $existingItems = @(
        Get-ChildItem -LiteralPath $script:projectRoot -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notin @("Bootstrap-AIWM.ps1", "Bootstrap-AIWM.bat", "Output") }
    )

    if ($existingItems.Count -gt 0) {
        throw "The current folder is not empty and is not a Git repository. Move the bootstrap files to an empty folder or use an existing AIWM repository folder."
    }

    Write-Step "Cloning repository into the current folder"
    Invoke-LoggedCommand -Command "git" -Arguments @("clone", $RepositoryUrl, ".") | Out-Null
    Set-Location -LiteralPath $script:projectRoot
}

function Stop-RunningApp {
    Write-Step "Stopping running AIWM processes"
    Get-Process -Name "AIWordPressManager.Desktop" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match "AIWordPressManager" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 700
}

function Clean-Restore-Build {
    $solution = Join-Path $script:projectRoot "AIWordPressManager.sln"
    if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) { throw "Solution not found: $solution" }

    Write-Step "Stopping .NET build servers"
    Invoke-LoggedCommand -Command "dotnet" -Arguments @("build-server", "shutdown") -IgnoreExitCode | Out-Null

    Write-Step "Removing bin and obj folders"
    Get-ChildItem -LiteralPath $script:projectRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @("bin", "obj") } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    Write-Step "Restoring NuGet packages"
    Invoke-LoggedCommand -Command "dotnet" -Arguments @("restore", $solution, "--force") | Out-Null

    Write-Step "Building $Configuration configuration"
    Invoke-LoggedCommand -Command "dotnet" -Arguments @("build", $solution, "-c", $Configuration, "--no-restore") | Out-Null
}

function Run-App {
    if ($NoRun) { return }
    $desktopProject = Join-Path $script:projectRoot "src\AIWordPressManager.Desktop\AIWordPressManager.Desktop.csproj"
    if (-not (Test-Path -LiteralPath $desktopProject -PathType Leaf)) { throw "Desktop project not found: $desktopProject" }
    Write-Step "Running AI WordPress Manager"
    Invoke-LoggedCommand -Command "dotnet" -Arguments @("run", "--no-build", "-c", $Configuration, "--project", $desktopProject) | Out-Null
}

function Complete-Run {
    param([string]$Status, [string]$Message)
    if (-not $script:latestOutput) { return }
    $summaryPath = Join-Path $script:latestOutput "Summary.txt"
    @(
        "AI WordPress Manager Bootstrap"
        "Status: $Status"
        "Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        "Repository: $RepositoryUrl"
        "Project: $script:projectRoot"
        "Configuration: $Configuration"
        "Message: $Message"
        "Log: $script:logPath"
    ) | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    Copy-Item -LiteralPath $summaryPath -Destination (Join-Path $script:historyOutput "Summary.txt") -Force
    try { Start-Process explorer.exe -ArgumentList $script:latestOutput | Out-Null }
    catch { Write-Host "Output folder: $script:latestOutput" -ForegroundColor Yellow }
}

try {
    $script:projectRoot = Resolve-SafePath -Path (Get-Location).ProviderPath
    Initialize-Output -Root $script:projectRoot

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " AI WordPress Manager - Current Folder Bootstrap" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host "Working folder: $script:projectRoot" -ForegroundColor Yellow

    Ensure-Prerequisites
    Prepare-Repository
    Initialize-Output -Root $script:projectRoot
    Stop-RunningApp
    Clean-Restore-Build
    Run-App

    $commit = (& git -C $script:projectRoot rev-parse --short HEAD).Trim()
    Complete-Run -Status "SUCCESS" -Message "Update/clone, build, and run completed at commit $commit."

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host " SUCCESS" -ForegroundColor Green
    Write-Host " Project: $script:projectRoot" -ForegroundColor Cyan
    Write-Host " Output : $script:latestOutput" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Green
}
catch {
    $exitCode = 1
    $message = $_.Exception.Message
    Write-Host ""
    Write-Host "[ERROR] $message" -ForegroundColor Red
    try { Write-Log -Message "[ERROR] $message" } catch { }
    try { Complete-Run -Status "FAILED" -Message $message } catch { }
}
finally {
    Wait-BeforeExit
}

exit $exitCode
