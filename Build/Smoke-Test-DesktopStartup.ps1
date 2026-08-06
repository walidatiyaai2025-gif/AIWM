[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [int]$ObservationSeconds = 15
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/AIWordPressManager.Desktop/AIWordPressManager.Desktop.csproj"
$outputFolder = Join-Path $repoRoot "src/AIWordPressManager.Desktop/bin/$Configuration/net8.0-windows"
$exePath = Join-Path $outputFolder "AIWordPressManager.Desktop.exe"

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Desktop project was not found: $projectPath"
}

if (-not (Test-Path -LiteralPath $exePath)) {
    Write-Host "Desktop executable is missing. Building the project first..." -ForegroundColor Yellow
    & dotnet build $projectPath --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Desktop build failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Desktop executable was not created: $exePath"
}

$stdoutPath = Join-Path $env:TEMP "aiwm-startup-smoke-stdout.log"
$stderrPath = Join-Path $env:TEMP "aiwm-startup-smoke-stderr.log"
Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue

Write-Host "Starting desktop smoke test: $exePath" -ForegroundColor Cyan
$process = Start-Process `
    -FilePath $exePath `
    -WorkingDirectory $outputFolder `
    -PassThru `
    -RedirectStandardOutput $stdoutPath `
    -RedirectStandardError $stderrPath

$observedWindowTitles = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$windowObserved = $false
$inputIdleObserved = $false

try {
    try {
        $inputIdleObserved = $process.WaitForInputIdle(10000)
    }
    catch {
        Write-Host "WaitForInputIdle was unavailable: $($_.Exception.Message)" -ForegroundColor Yellow
    }

    $deadline = [DateTime]::UtcNow.AddSeconds([Math]::Max(5, $ObservationSeconds))
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()

        if ($process.HasExited) {
            $stdout = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Raw } else { "" }
            $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { "" }
            throw @"
Desktop process exited during the startup observation window.
Exit code: $($process.ExitCode)
STDOUT:
$stdout
STDERR:
$stderr
"@
        }

        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            $windowObserved = $true
        }

        $title = $process.MainWindowTitle
        if (-not [string]::IsNullOrWhiteSpace($title)) {
            [void]$observedWindowTitles.Add($title)
            $windowObserved = $true
        }
    }

    $titles = if ($observedWindowTitles.Count -gt 0) {
        [string]::Join('; ', $observedWindowTitles)
    }
    else {
        '<none>'
    }

    Write-Host "Observed window titles: $titles" -ForegroundColor DarkGray
    Write-Host "Input idle observed: $inputIdleObserved; top-level window observed: $windowObserved" -ForegroundColor DarkGray
    Write-Host "Startup smoke test passed. The desktop process initialized and remained alive for $ObservationSeconds seconds." -ForegroundColor Green
}
finally {
    if (-not $process.HasExited) {
        try {
            $process.CloseMainWindow() | Out-Null
            if (-not $process.WaitForExit(3000)) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
        }
        catch {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}
