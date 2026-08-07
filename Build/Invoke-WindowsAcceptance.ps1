[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [int]$ObservationSeconds = 20,
    [string]$OutputDirectory,
    [string]$SourceBranch,
    [string]$SourceCommit
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$smokeTestPath = Join-Path $PSScriptRoot "Smoke-Test-DesktopStartup.ps1"
$desktopOutput = Join-Path $repoRoot "src/AIWordPressManager.Desktop/bin/$Configuration/net8.0-windows"
$desktopExecutable = Join-Path $desktopOutput "AIWordPressManager.Desktop.exe"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "AcceptanceResults"
}
if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}

if (-not (Test-Path -LiteralPath $smokeTestPath)) {
    throw "Startup smoke test was not found: $smokeTestPath"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$startedAtUtc = [DateTimeOffset]::UtcNow
$reportId = [Guid]::NewGuid().ToString("N")
$status = "Passed"
$failure = $null
$smokeOutput = ""

try {
    $smokeOutput = (& $smokeTestPath -Configuration $Configuration -ObservationSeconds $ObservationSeconds 2>&1 | Out-String).Trim()
}
catch {
    $status = "Failed"
    $failure = $_.Exception.ToString()
    $smokeOutput = ($_ | Out-String).Trim()
}

$completedAtUtc = [DateTimeOffset]::UtcNow

if ([string]::IsNullOrWhiteSpace($SourceBranch)) {
    $SourceBranch = if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_HEAD_REF)) {
        $env:GITHUB_HEAD_REF
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:GITHUB_REF_NAME)) {
        $env:GITHUB_REF_NAME
    }
    else {
        "local"
    }
}

if ([string]::IsNullOrWhiteSpace($SourceCommit)) {
    $SourceCommit = if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) {
        $env:GITHUB_SHA
    }
    else {
        try { (& git -C $repoRoot rev-parse HEAD 2>$null).Trim() } catch { "unknown" }
    }
}

$executableExists = Test-Path -LiteralPath $desktopExecutable
$executableHash = if ($executableExists) {
    (Get-FileHash -LiteralPath $desktopExecutable -Algorithm SHA256).Hash
}
else {
    $null
}
$fileVersion = if ($executableExists) {
    (Get-Item -LiteralPath $desktopExecutable).VersionInfo.FileVersion
}
else {
    $null
}

$report = [ordered]@{
    SchemaVersion = 1
    ReportId = $reportId
    Status = $status
    StartedAtUtc = $startedAtUtc.ToString("O")
    CompletedAtUtc = $completedAtUtc.ToString("O")
    DurationSeconds = [Math]::Round(($completedAtUtc - $startedAtUtc).TotalSeconds, 3)
    Configuration = $Configuration
    ObservationSeconds = $ObservationSeconds
    SourceBranch = $SourceBranch
    SourceCommit = $SourceCommit
    OperatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    OSArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    ProcessArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    DotNetRuntime = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    DesktopExecutable = $desktopExecutable
    DesktopExecutableExists = $executableExists
    DesktopFileVersion = $fileVersion
    DesktopSha256 = $executableHash
    SmokeOutput = $smokeOutput
    Failure = $failure
}

$jsonPath = Join-Path $OutputDirectory "windows-acceptance.json"
$markdownPath = Join-Path $OutputDirectory "windows-acceptance.md"
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$failureText = if ([string]::IsNullOrWhiteSpace($failure)) { "None" } else { $failure }
$hashText = if ([string]::IsNullOrWhiteSpace($executableHash)) { "Unavailable" } else { $executableHash }
$versionText = if ([string]::IsNullOrWhiteSpace($fileVersion)) { "Unavailable" } else { $fileVersion }
$codeFence = '```'
$markdown = @"
# Windows Acceptance Evidence

- Status: **$status**
- Report ID: $reportId
- Started UTC: $($startedAtUtc.ToString("O"))
- Completed UTC: $($completedAtUtc.ToString("O"))
- Configuration: $Configuration
- Source branch: $SourceBranch
- Source commit: $SourceCommit
- Desktop version: $versionText
- Desktop SHA-256: $hashText
- OS: $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)

## Startup smoke output

${codeFence}text
$smokeOutput
$codeFence

## Failure

${codeFence}text
$failureText
$codeFence
"@
Set-Content -LiteralPath $markdownPath -Value $markdown -Encoding UTF8

Write-Host "Windows acceptance JSON: $jsonPath" -ForegroundColor Cyan
Write-Host "Windows acceptance Markdown: $markdownPath" -ForegroundColor Cyan

if ($status -ne "Passed") {
    throw "Windows acceptance failed. Review $jsonPath."
}

Write-Host "Windows acceptance evidence completed successfully." -ForegroundColor Green
