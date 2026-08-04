[CmdletBinding()]
param(
    [string]$InstallPath = "$env:USERPROFILE\AIWordPressManager",
    [string]$RepositoryUrl = "https://github.com/walidatiyaai2025-gif/AIWM.git",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$NoRun
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Step([string]$Message) {
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Assert-Command([string]$Name, [string]$InstallHint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name is not installed or is not available in PATH. $InstallHint"
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
}

function Remove-BuildFolders([string]$Root) {
    Write-Step "Removing bin and obj folders"
    Get-ChildItem -Path $Root -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @("bin", "obj") } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

try {
    Write-Host "AI WordPress Manager - First Time Setup" -ForegroundColor Yellow
    Write-Host "Install path: $InstallPath"

    Assert-Command "git" "Install Git for Windows, then open a new PowerShell window."
    Assert-Command "dotnet" "Install the .NET 8 SDK, then open a new PowerShell window."

    $sdkVersions = & dotnet --list-sdks
    if (-not ($sdkVersions -match '^8\.')) {
        throw ".NET 8 SDK was not found. Install .NET 8 SDK before continuing."
    }

    Stop-AppProcesses

    if (Test-Path (Join-Path $InstallPath ".git")) {
        Write-Step "Existing repository found - updating it"
        Set-Location $InstallPath
        & git checkout main
        if ($LASTEXITCODE -ne 0) { throw "git checkout main failed." }
        & git pull --ff-only origin main
        if ($LASTEXITCODE -ne 0) { throw "git pull failed. Resolve local changes and run the script again." }
    }
    elseif (Test-Path $InstallPath) {
        $existingItems = Get-ChildItem -Path $InstallPath -Force -ErrorAction SilentlyContinue
        if ($existingItems.Count -gt 0) {
            throw "Install path already exists and is not empty: $InstallPath"
        }

        Write-Step "Cloning repository"
        & git clone $RepositoryUrl $InstallPath
        if ($LASTEXITCODE -ne 0) { throw "git clone failed." }
        Set-Location $InstallPath
    }
    else {
        Write-Step "Cloning repository"
        $parent = Split-Path -Parent $InstallPath
        if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        & git clone $RepositoryUrl $InstallPath
        if ($LASTEXITCODE -ne 0) { throw "git clone failed." }
        Set-Location $InstallPath
    }

    if (-not (Test-Path ".\AIWordPressManager.sln")) {
        throw "AIWordPressManager.sln was not found in $InstallPath."
    }

    Write-Step "Stopping .NET build servers"
    & dotnet build-server shutdown | Out-Host

    Remove-BuildFolders $InstallPath

    Write-Step "Restoring NuGet packages"
    & dotnet restore ".\AIWordPressManager.sln" --force
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

    Write-Step "Building $Configuration configuration"
    & dotnet build ".\AIWordPressManager.sln" -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }

    if (-not $NoRun) {
        Write-Step "Starting AI WordPress Manager"
        & dotnet run --no-build -c $Configuration --project ".\src\AIWordPressManager.Desktop\AIWordPressManager.Desktop.csproj"
        if ($LASTEXITCODE -ne 0) { throw "Application exited with code $LASTEXITCODE." }
    }

    Write-Host "`nSetup completed successfully." -ForegroundColor Green
}
catch {
    Write-Host "`n[ERROR] $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Install Git for Windows and .NET 8 SDK if either prerequisite is missing." -ForegroundColor Yellow
    exit 1
}
