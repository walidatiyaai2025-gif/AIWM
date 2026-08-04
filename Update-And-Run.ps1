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

$LogPath = Join-Path $ProjectPath "update-and-run.log"

function Write-Step([string]$Message) {
    $line = "[$(Get-Date -Format 'HH:mm:ss')] $Message"
    Write-Host "`n==> $Message" -ForegroundColor Cyan
    Add-Content -Path $LogPath -Value $line -Encoding UTF8
}

function Assert-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name is not installed or is not available in PATH."
    }
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

try {
    Set-Content -Path $LogPath -Value "AI WordPress Manager update started at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding UTF8

    Assert-Command "git"
    Assert-Command "dotnet"

    $ProjectPath = [System.IO.Path]::GetFullPath($ProjectPath)
    if (-not (Test-Path (Join-Path $ProjectPath ".git"))) {
        throw "Git repository was not found at: $ProjectPath"
    }
    if (-not (Test-Path (Join-Path $ProjectPath "AIWordPressManager.sln"))) {
        throw "AIWordPressManager.sln was not found at: $ProjectPath"
    }

    Set-Location $ProjectPath
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
    & git checkout main 2>&1 | Tee-Object -FilePath $LogPath -Append | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "git checkout main failed." }

    Write-Step "Fetching latest commits"
    & git fetch origin 2>&1 | Tee-Object -FilePath $LogPath -Append | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "git fetch failed." }

    Write-Step "Updating main branch"
    & git pull --ff-only origin main 2>&1 | Tee-Object -FilePath $LogPath -Append | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "git pull failed. Resolve branch divergence or local changes first." }

    Write-Step "Stopping .NET build servers"
    & dotnet build-server shutdown 2>&1 | Tee-Object -FilePath $LogPath -Append | Out-Host

    if (-not $SkipClean) {
        Write-Step "Removing bin and obj folders"
        Get-ChildItem -Path $ProjectPath -Directory -Recurse -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -in @("bin", "obj") } |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Step "Restoring NuGet packages"
    & dotnet restore ".\AIWordPressManager.sln" --force 2>&1 |
        Tee-Object -FilePath $LogPath -Append | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed. See $LogPath" }

    Write-Step "Building $Configuration configuration"
    & dotnet build ".\AIWordPressManager.sln" -c $Configuration --no-restore 2>&1 |
        Tee-Object -FilePath $LogPath -Append | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed. See $LogPath" }

    $commit = (& git rev-parse --short HEAD).Trim()
    Write-Step "Build completed successfully at commit $commit"

    if (-not $NoRun) {
        Write-Step "Starting AI WordPress Manager"
        & dotnet run --no-build -c $Configuration --project ".\src\AIWordPressManager.Desktop\AIWordPressManager.Desktop.csproj" 2>&1 |
            Tee-Object -FilePath $LogPath -Append | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Application exited with code $LASTEXITCODE. See $LogPath" }
    }
}
catch {
    $message = "[ERROR] $($_.Exception.Message)"
    Write-Host "`n$message" -ForegroundColor Red
    Add-Content -Path $LogPath -Value $message -Encoding UTF8

    if (Test-Path $LogPath) {
        Write-Host "Log: $LogPath" -ForegroundColor Yellow
    }
    exit 1
}
