[CmdletBinding()]
param(
    [string]$InstallRoot = "C:\Apps",
    [string]$RepositoryUrl = "https://github.com/walidatiyaai2025-gif/AIWM.git",
    [string]$RepositoryFolderName = "AIWM",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$NoRun,
    [switch]$SkipPrerequisiteInstall,
    [switch]$ResetLocalChanges,
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
    param([string]$Message = "Press ENTER to close this window...")

    if ($NoPause) { return }
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor DarkCyan
    Write-Host $Message -ForegroundColor Magenta
    Write-Host "============================================================" -ForegroundColor DarkCyan
    try { [void](Read-Host) }
    catch {
        try { [void]$Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") }
        catch { Start-Sleep -Seconds 15 }
    }
}

function Resolve-SafePath {
    param([Parameter(Mandatory)][string]$Path)

    $clean = $Path.Trim().Replace('"', '').Replace("'", '')
    if ([string]::IsNullOrWhiteSpace($clean)) {
        throw "A required path is empty."
    }
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

    foreach ($directory in @($outputRoot, $script:latestOutput, $historyRoot, $script:historyOutput)) {
        Ensure-Directory -Path $directory
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
    if (-not [string]::IsNullOrWhiteSpace($script:logPath)) {
        Add-Content -LiteralPath $script:logPath -Value $line -Encoding UTF8
    }
    if (-not [string]::IsNullOrWhiteSpace($script:historyLogPath)) {
        Add-Content -LiteralPath $script:historyLogPath -Value $line -Encoding UTF8
    }
}

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
    Write-Log -Message $Message
}

function Refresh-PathEnvironment {
    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = "$machinePath;$userPath"
}

function Test-CommandAvailable {
    param([Parameter(Mandatory)][string]$Name)
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Install-WingetPackage {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][string]$DisplayName
    )

    if ($SkipPrerequisiteInstall) {
        throw "$DisplayName is missing and prerequisite installation was skipped."
    }
    if (-not (Test-CommandAvailable -Name "winget")) {
        throw "winget is required to install $DisplayName automatically. Install Microsoft App Installer, then rerun this script."
    }

    Write-Step "Installing $DisplayName"
    & winget install --id $Id --exact --silent --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install $DisplayName with winget. Exit code: $LASTEXITCODE"
    }
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

    if (-not (Test-CommandAvailable -Name "git")) {
        throw "Git is still unavailable after prerequisite setup."
    }
    if (-not (Test-CommandAvailable -Name "dotnet")) {
        throw ".NET SDK is still unavailable after prerequisite setup."
    }

    $sdkList = & dotnet --list-sdks
    if ($LASTEXITCODE -ne 0 -or -not ($sdkList -match '^8\.')) {
        Install-WingetPackage -Id "Microsoft.DotNet.SDK.8" -DisplayName ".NET 8 SDK"
        $sdkList = & dotnet --list-sdks
        if (-not ($sdkList -match '^8\.')) {
            throw ".NET 8 SDK is required but was not detected."
        }
    }

    Write-Log -Message "Git: $((& git --version) -join ' ')"
    Write-Log -Message ".NET SDKs: $($sdkList -join '; ')"
}

function Invoke-LoggedCommand {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter()][string[]]$Arguments = @(),
        [switch]$IgnoreExitCode
    )

    Write-Log -Message ("RUN: {0} {1}" -f $Command, ($Arguments -join ' '))
    $output = & $Command @Arguments 2>&1
    $code = $LASTEXITCODE
    $output | Out-Host
    if ($null -ne $output) {
        $output | Add-Content -LiteralPath $script:logPath -Encoding UTF8
        $output | Add-Content -LiteralPath $script:historyLogPath -Encoding UTF8
    }
    if (-not $IgnoreExitCode -and $code -ne 0) {
        throw "$Command failed with exit code $code."
    }
    return $code
}

function Stop-RunningApp {
    Write-Step "Stopping running AIWM processes"

    Get-Process -Name "AIWordPressManager.Desktop" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -match "AIWordPressManager" -or
            $_.CommandLine -match "AIWordPressManager.Desktop.csproj"
        } |
        ForEach-Object {
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }

    Start-Sleep -Milliseconds 700
}

function Clone-Or-UpdateRepository {
    $installRootFull = Resolve-SafePath -Path $InstallRoot
    Ensure-Directory -Path $installRootFull

    $script:projectRoot = Join-Path $installRootFull $RepositoryFolderName

    if (-not (Test-Path -LiteralPath $script:projectRoot -PathType Container)) {
        Write-Step "Cloning AIWM repository"
        Invoke-LoggedCommand -Command "git" -Arguments @("clone", $RepositoryUrl, $script:projectRoot)
    }
    elseif (-not (Test-Path -LiteralPath (Join-Path $script:projectRoot ".git") -PathType Container)) {
        throw "The target folder exists but is not a Git repository: $script:projectRoot"
    }

    Set-Location -LiteralPath $script:projectRoot

    Write-Step "Updating repository"
    Invoke-LoggedCommand -Command "git" -Arguments @("fetch", "origin", "--prune")

    if ($ResetLocalChanges) {
        Write-Step "Resetting local changes to origin/main"
        Invoke-LoggedCommand -Command "git" -Arguments @("checkout", "main")
        Invoke-LoggedCommand -Command "git" -Arguments @("reset", "--hard", "origin/main")
        Invoke-LoggedCommand -Command "git" -Arguments @("clean", "-fd")
    }
    else {
        $changes = & git status --porcelain
        if ($LASTEXITCODE -ne 0) { throw "git status failed." }
        if ($changes) {
            Write-Host "Local changes detected:" -ForegroundColor Yellow
            $changes | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
            throw "Commit or stash local changes, or rerun with -ResetLocalChanges."
        }
        Invoke-LoggedCommand -Command "git" -Arguments @("checkout", "main")
        Invoke-LoggedCommand -Command "git" -Arguments @("pull", "--ff-only", "origin", "main")
    }
}

function Clean-Restore-Build {
    Write-Step "Stopping .NET build servers"
    Invoke-LoggedCommand -Command "dotnet" -Arguments @("build-server", "shutdown") -IgnoreExitCode | Out-Null

    Write-Step "Removing bin and obj folders"
    Get-ChildItem -LiteralPath $script:projectRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @("bin", "obj") } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    $solutionPath = Join-Path $script:projectRoot "AIWordPressManager.sln"
    if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
        throw "Solution file was not found: $solutionPath"
    }

    Write-Step "Restoring NuGet packages"
    Invoke-LoggedCommand -Command "dotnet" -Arguments @("restore", $solutionPath, "--force")

    Write-Step "Building $Configuration configuration"
    Invoke-LoggedCommand -Command "dotnet" -Arguments @("build", $solutionPath, "-c", $Configuration, "--no-restore")
}

function Run-Application {
    if ($NoRun) {
        Write-Step "Build completed; application launch skipped"
        return
    }

    $desktopProject = Join-Path $script:projectRoot "src\AIWordPressManager.Desktop\AIWordPressManager.Desktop.csproj"
    if (-not (Test-Path -LiteralPath $desktopProject -PathType Leaf)) {
        throw "Desktop project was not found: $desktopProject"
    }

    Write-Step "Running AI WordPress Manager"
    Invoke-LoggedCommand -Command "dotnet" -Arguments @(
        "run", "--no-build", "-c", $Configuration,
        "--project", $desktopProject
    )
}

function Complete-Run {
    param(
        [Parameter(Mandatory)][string]$Status,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not [string]::IsNullOrWhiteSpace($script:latestOutput)) {
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

        if (-not [string]::IsNullOrWhiteSpace($script:historyOutput)) {
            Copy-Item -LiteralPath $summaryPath -Destination (Join-Path $script:historyOutput "Summary.txt") -Force
        }

        try { Start-Process explorer.exe -ArgumentList $script:latestOutput | Out-Null }
        catch { Write-Host "Output folder: $script:latestOutput" -ForegroundColor Yellow }
    }
}

try {
    $resolvedInstallRoot = Resolve-SafePath -Path $InstallRoot
    Ensure-Directory -Path $resolvedInstallRoot

    # Create bootstrap logs before cloning, then recreate them inside the repository after clone/update.
    Initialize-Output -Root $resolvedInstallRoot
    Ensure-Prerequisites
    Clone-Or-UpdateRepository
    Initialize-Output -Root $script:projectRoot
    Stop-RunningApp
    Clean-Restore-Build
    Run-Application

    $commit = (& git -C $script:projectRoot rev-parse --short HEAD).Trim()
    Complete-Run -Status "SUCCESS" -Message "Bootstrap, build, and run completed at commit $commit."
    Write-Host ""
    Write-Host "SUCCESS: AIWM is ready." -ForegroundColor Green
    Write-Host "Project: $script:projectRoot" -ForegroundColor Cyan
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
