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

function Wait-BeforeExit {
    param([string]$Message = "Press ENTER to close this window...")

    if ($NoPause) { return }
    Write-Host ""
    Write-Host "==============================================" -ForegroundColor DarkCyan
    Write-Host $Message -ForegroundColor Magenta
    Write-Host "==============================================" -ForegroundColor DarkCyan
    try { [void](Read-Host) }
    catch {
        try { [void]$Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") }
        catch { Start-Sleep -Seconds 15 }
    }
}

function Resolve-ProjectRoot {
    param([AllowNull()][AllowEmptyString()][string]$RequestedPath)

    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) { $candidates.Add($RequestedPath) }
    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) { $candidates.Add($PSScriptRoot) }

    $scriptPath = $MyInvocation.ScriptName
    if (-not [string]::IsNullOrWhiteSpace($scriptPath)) {
        $parent = Split-Path -Parent $scriptPath
        if (-not [string]::IsNullOrWhiteSpace($parent)) { $candidates.Add($parent) }
    }

    $location = (Get-Location).ProviderPath
    if (-not [string]::IsNullOrWhiteSpace($location)) { $candidates.Add($location) }

    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }

        # Remove literal quote characters that can be introduced by malformed BAT arguments.
        $clean = $candidate.Trim()
        $clean = $clean.Replace('"', '').Replace("'", '')
        $clean = $clean.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
        if ([string]::IsNullOrWhiteSpace($clean)) { continue }

        try { $full = [System.IO.Path]::GetFullPath($clean) }
        catch { continue }

        if (Test-Path -LiteralPath $full -PathType Container) { return $full }
    }

    throw "Could not resolve a valid project root. Pass -ProjectPath with the repository folder."
}

$exitCode = 0
$ProjectRoot = $null
$LatestOutput = $null
$HistoryOutput = $null
$LogPath = $null
$HistoryLogPath = $null
$SummaryPath = $null

function Write-LogLine {
    param([Parameter(Mandatory)][string]$Message)

    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        Add-Content -LiteralPath $LogPath -Value $Message -Encoding UTF8
    }
    if (-not [string]::IsNullOrWhiteSpace($HistoryLogPath)) {
        Add-Content -LiteralPath $HistoryLogPath -Value $Message -Encoding UTF8
    }
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
    $code = $LASTEXITCODE
    $output | Out-Host
    if ($null -ne $output) {
        $output | Add-Content -LiteralPath $LogPath -Encoding UTF8
        $output | Add-Content -LiteralPath $HistoryLogPath -Encoding UTF8
    }
    return $code
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
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

    Start-Sleep -Milliseconds 700
}

function Complete-Run {
    param(
        [Parameter(Mandatory)][string]$Status,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not [string]::IsNullOrWhiteSpace($SummaryPath)) {
        @(
            "AI WordPress Manager"
            "Status: $Status"
            "Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
            "Project: $ProjectRoot"
            "Configuration: $Configuration"
            "Message: $Message"
            "Latest output: $LatestOutput"
            "History output: $HistoryOutput"
        ) | Set-Content -LiteralPath $SummaryPath -Encoding UTF8

        Copy-Item -LiteralPath $SummaryPath -Destination (Join-Path $HistoryOutput "Summary.txt") -Force
    }

    if (-not [string]::IsNullOrWhiteSpace($LatestOutput)) {
        try { Start-Process explorer.exe -ArgumentList $LatestOutput | Out-Null }
        catch { Write-Host "Output folder: $LatestOutput" -ForegroundColor Yellow }
    }
}

try {
    $ProjectRoot = Resolve-ProjectRoot -RequestedPath $ProjectPath

    $OutputRoot = Join-Path $ProjectRoot "Output"
    $LatestOutput = Join-Path $OutputRoot "Latest"
    $HistoryRoot = Join-Path $OutputRoot "History"
    $HistoryOutput = Join-Path $HistoryRoot (Get-Date -Format "yyyy-MM-dd_HH-mm-ss")

    foreach ($directory in @($OutputRoot, $LatestOutput, $HistoryRoot, $HistoryOutput)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $LogPath = Join-Path $LatestOutput "update-and-run.log"
    $HistoryLogPath = Join-Path $HistoryOutput "update-and-run.log"
    $SummaryPath = Join-Path $LatestOutput "Summary.txt"

    $startMessage = "AI WordPress Manager update started at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Set-Content -LiteralPath $LogPath -Value $startMessage -Encoding UTF8
    Set-Content -LiteralPath $HistoryLogPath -Value $startMessage -Encoding UTF8

    Assert-Command "git"
    Assert-Command "dotnet"

    if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot ".git"))) {
        throw "Git repository was not found at: $ProjectRoot"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot "AIWordPressManager.sln"))) {
        throw "AIWordPressManager.sln was not found at: $ProjectRoot"
    }

    Set-Location -LiteralPath $ProjectRoot
    Stop-AppProcesses

    Write-Step "Checking local repository status"
    $localChanges = & git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw "git status failed." }
    if ($localChanges) {
        Write-Host "Local changes were detected:" -ForegroundColor Yellow
        $localChanges | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
        throw "Commit or stash local changes before updating."
    }

    Write-Step "Switching to main branch"
    if ((Invoke-LoggedCommand -Command "git" -Arguments @("checkout", "main")) -ne 0) { throw "git checkout main failed." }

    Write-Step "Fetching latest commits"
    if ((Invoke-LoggedCommand -Command "git" -Arguments @("fetch", "origin")) -ne 0) { throw "git fetch failed." }

    Write-Step "Updating main branch"
    if ((Invoke-LoggedCommand -Command "git" -Arguments @("pull", "--ff-only", "origin", "main")) -ne 0) {
        throw "git pull failed. Resolve branch divergence or local changes first."
    }

    Write-Step "Stopping .NET build servers"
    [void](Invoke-LoggedCommand -Command "dotnet" -Arguments @("build-server", "shutdown"))

    if (-not $SkipClean) {
        Write-Step "Removing bin and obj folders"
        Get-ChildItem -LiteralPath $ProjectRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
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
        if ($runCode -ne 0) { throw "Application exited with code $runCode. See $LogPath" }
    }

    Complete-Run -Status "SUCCESS" -Message "Build and update completed successfully at commit $commit."
}
catch {
    $exitCode = 1
    $message = "[ERROR] $($_.Exception.Message)"
    Write-Host "`n$message" -ForegroundColor Red

    try { Write-LogLine -Message $message }
    catch { Write-Host "Could not write the log, but the original error is shown above." -ForegroundColor Yellow }

    try { Complete-Run -Status "FAILED" -Message $message } catch { }
}
finally {
    Wait-BeforeExit
}

exit $exitCode
