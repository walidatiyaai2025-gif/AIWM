[CmdletBinding()]
param(
    [string]$ProjectPath = $PSScriptRoot,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$SkipClean,
    [switch]$NoRun
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-SafePath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $clean = $Path.Trim().Trim('"').Trim("'")
    foreach ($invalid in [System.IO.Path]::GetInvalidPathChars()) {
        $clean = $clean.Replace([string]$invalid, "")
    }

    if ([string]::IsNullOrWhiteSpace($clean)) {
        throw "The project path is empty after path sanitization."
    }

    return [System.IO.Path]::GetFullPath($clean)
}

$ProjectPath = Resolve-SafePath -Path $ProjectPath
$OutputRoot = Join-Path $ProjectPath "Output"
$LatestOutput = Join-Path $OutputRoot "Latest"
$HistoryRoot = Join-Path $OutputRoot "History"
$RunStamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$HistoryOutput = Join-Path $HistoryRoot $RunStamp

foreach ($directory in @($OutputRoot, $LatestOutput, $HistoryRoot, $HistoryOutput)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$LogPath = Join-Path $LatestOutput "update-and-run.log"
$HistoryLogPath = Join-Path $HistoryOutput "update-and-run.log"
$SummaryPath = Join-Path $LatestOutput "Summary.txt"

function Write-LogLine {
    param([Parameter(Mandatory)][string]$Message)

    Add-Content -LiteralPath $LogPath -Value $Message -Encoding UTF8
    Add-Content -LiteralPath $HistoryLogPath -Value $Message -Encoding UTF8
}

function Write-Step([string]$Message) {
    $line = "[$(Get-Date -Format 'HH:mm:ss')] $Message"
    Write-Host "`n==> $Message" -ForegroundColor Cyan
    Write-LogLine -Message $line
}

function Assert-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name is not installed or is not available in PATH."
    }
}

function Invoke-LoggedCommand {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter()][string[]]$Arguments = @()
    )

    $output = & $Command @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $output | Tee-Object -FilePath $LogPath -Append | Out-Host
    $output | Add-Content -LiteralPath $HistoryLogPath -Encoding UTF8
    return $exitCode
}

function Stop-AppProcesses {
    Write-Step "Stopping running AI WordPress Manager processes"

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

function Complete-Run {
    param(
        [Parameter(Mandatory)][string]$Status,
        [Parameter(Mandatory)][string]$Message
    )

    $summary = @(
        "AI WordPress Manager",
        "Status: $Status",
        "Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
        "Project: $ProjectPath",
        "Configuration: $Configuration",
        "Message: $Message",
        "Latest output: $LatestOutput",
        "History output: $HistoryOutput"
    ) -join [Environment]::NewLine

    Set-Content -LiteralPath $SummaryPath -Value $summary -Encoding UTF8
    Copy-Item -LiteralPath $SummaryPath -Destination (Join-Path $HistoryOutput "Summary.txt") -Force

    try {
        Start-Process explorer.exe -ArgumentList ('"{0}"' -f $LatestOutput) | Out-Null
    }
    catch {
        Write-Host "Output folder: $LatestOutput" -ForegroundColor Yellow
    }
}

try {
    $startMessage = "AI WordPress Manager update started at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Set-Content -LiteralPath $LogPath -Value $startMessage -Encoding UTF8
    Set-Content -LiteralPath $HistoryLogPath -Value $startMessage -Encoding UTF8

    Assert-Command "git"
    Assert-Command "dotnet"

    if (-not (Test-Path -LiteralPath (Join-Path $ProjectPath ".git"))) {
        throw "Git repository was not found at: $ProjectPath"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $ProjectPath "AIWordPressManager.sln"))) {
        throw "AIWordPressManager.sln was not found at: $ProjectPath"
    }

    Set-Location -LiteralPath $ProjectPath
    Stop-AppProcesses

    Write-Step "Checking local repository status"
    $localChanges = & git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw "git status failed." }
    if ($localChanges) {
        Write-Host "Local changes were detected. They will not be overwritten:" -ForegroundColor Yellow
        $localChanges | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
        throw "Commit or stash local changes before updating."
    }

    Write-Step "Switching to main branch"
    if ((Invoke-LoggedCommand -Command "git" -Arguments @("checkout", "main")) -ne 0) {
        throw "git checkout main failed."
    }

    Write-Step "Fetching latest commits"
    if ((Invoke-LoggedCommand -Command "git" -Arguments @("fetch", "origin")) -ne 0) {
        throw "git fetch failed."
    }

    Write-Step "Updating main branch"
    if ((Invoke-LoggedCommand -Command "git" -Arguments @("pull", "--ff-only", "origin", "main")) -ne 0) {
        throw "git pull failed. Resolve branch divergence or local changes first."
    }

    Write-Step "Stopping .NET build servers"
    [void](Invoke-LoggedCommand -Command "dotnet" -Arguments @("build-server", "shutdown"))

    if (-not $SkipClean) {
        Write-Step "Removing bin and obj folders"
        Get-ChildItem -LiteralPath $ProjectPath -Directory -Recurse -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -in @("bin", "obj") } |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Step "Restoring NuGet packages"
    if ((Invoke-LoggedCommand -Command "dotnet" -Arguments @("restore", ".\AIWordPressManager.sln", "--force")) -ne 0) {
        throw "dotnet restore failed. See $LogPath"
    }

    Write-Step "Building $Configuration configuration"
    if ((Invoke-LoggedCommand -Command "dotnet" -Arguments @("build", ".\AIWordPressManager.sln", "-c", $Configuration, "--no-restore")) -ne 0) {
        throw "dotnet build failed. See $LogPath"
    }

    $commit = (& git rev-parse --short HEAD).Trim()
    Write-Step "Build completed successfully at commit $commit"

    if (-not $NoRun) {
        Write-Step "Starting AI WordPress Manager"
        $runCode = Invoke-LoggedCommand -Command "dotnet" -Arguments @(
            "run", "--no-build", "-c", $Configuration,
            "--project", ".\src\AIWordPressManager.Desktop\AIWordPressManager.Desktop.csproj"
        )
        if ($runCode -ne 0) {
            throw "Application exited with code $runCode. See $LogPath"
        }
    }

    Complete-Run -Status "SUCCESS" -Message "Build and update completed successfully at commit $commit."
}
catch {
    $message = "[ERROR] $($_.Exception.Message)"
    Write-Host "`n$message" -ForegroundColor Red

    try {
        Write-LogLine -Message $message
    }
    catch {
        Write-Host "Could not write to the log file. Output folder: $LatestOutput" -ForegroundColor Yellow
    }

    Complete-Run -Status "FAILED" -Message $message
    exit 1
}
