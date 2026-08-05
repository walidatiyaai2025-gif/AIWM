[CmdletBinding()]
param(
    [AllowNull()]
    [AllowEmptyString()]
    [string]$ProjectPath,
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

    $candidates = @(
        $RequestedPath,
        $PSScriptRoot,
        (Split-Path -Parent $MyInvocation.ScriptName),
        (Get-Location).ProviderPath
    )

    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        $clean = $candidate.Trim().Trim('"').Trim("'")
        if ([string]::IsNullOrWhiteSpace($clean)) { continue }

        try { $full = [System.IO.Path]::GetFullPath($clean) }
        catch { continue }

        if (Test-Path -LiteralPath $full -PathType Container) {
            return $full
        }
    }

    throw "Could not resolve the project root."
}

function Initialize-OutputContext {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$ScriptName
    )

    $outputRoot = Join-Path $Root "Output"
    $latest = Join-Path $outputRoot "Latest"
    $historyRoot = Join-Path $outputRoot "History"
    $history = Join-Path $historyRoot (Get-Date -Format "yyyy-MM-dd_HH-mm-ss")

    foreach ($folder in @($outputRoot, $latest, $historyRoot, $history)) {
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
    }

    $safeName = [System.IO.Path]::GetFileNameWithoutExtension($ScriptName)

    [pscustomobject]@{
        ProjectRoot = $Root
        LatestOutput = $latest
        HistoryOutput = $history
        LogPath = Join-Path $latest "$safeName.log"
        HistoryLogPath = Join-Path $history "$safeName.log"
        SummaryPath = Join-Path $latest "Summary.txt"
    }
}

function Add-Log {
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][string]$Message
    )

    Add-Content -LiteralPath $Context.LogPath -Value $Message -Encoding UTF8
    Add-Content -LiteralPath $Context.HistoryLogPath -Value $Message -Encoding UTF8
}

function Complete-Output {
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][string]$Status,
        [Parameter(Mandatory)][string]$Message
    )

    @(
        "AI WordPress Manager"
        "Status: $Status"
        "Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        "Project: $($Context.ProjectRoot)"
        "Message: $Message"
        "Latest output: $($Context.LatestOutput)"
        "History output: $($Context.HistoryOutput)"
    ) | Set-Content -LiteralPath $Context.SummaryPath -Encoding UTF8

    Copy-Item -LiteralPath $Context.SummaryPath `
        -Destination (Join-Path $Context.HistoryOutput "Summary.txt") `
        -Force

    try {
        Start-Process explorer.exe -ArgumentList ('"{0}"' -f $Context.LatestOutput) | Out-Null
    }
    catch {
        Write-Host "Output folder: $($Context.LatestOutput)" -ForegroundColor Yellow
    }
}

$exitCode = 0
$context = $null

try {
    $root = Resolve-ProjectRoot -RequestedPath $ProjectPath
    $context = Initialize-OutputContext -Root $root -ScriptName $MyInvocation.MyCommand.Name

    $reportPath = Join-Path $context.LatestOutput "Root-PowerShell-Path-Audit.md"
    $historyReportPath = Join-Path $context.HistoryOutput "Root-PowerShell-Path-Audit.md"

    Write-Host ""
    Write-Host "Validating root PowerShell scripts..." -ForegroundColor Cyan
    Write-Host "Project: $root" -ForegroundColor DarkGray
    Write-Host ""

    $scripts = @(
        Get-ChildItem -LiteralPath $root -File -Filter "*.ps1" -ErrorAction Stop |
        Sort-Object Name
    )

    $results = foreach ($script in $scripts) {
        $content = Get-Content -LiteralPath $script.FullName -Raw -ErrorAction Stop
        $issues = [System.Collections.Generic.List[string]]::new()

        if ($content -match '\$PSScriptRoot\s*\+') {
            $issues.Add("Manual concatenation with `$PSScriptRoot; use Join-Path.")
        }
        if ($content -match '\$ProjectPath\s*\+') {
            $issues.Add("Manual concatenation with `$ProjectPath; use Join-Path.")
        }
        if ($content -match 'Add-Content\s+-Path\s+\$LogPath') {
            $issues.Add("Use -LiteralPath and sanitize LogPath before writing.")
        }
        if ($content -match '\$LogPath\s*=\s*"\$[^\r\n]+\\') {
            $issues.Add("LogPath is built with interpolation instead of Join-Path.")
        }

        $status = if ($issues.Count -eq 0) { "PASS" } else { "REVIEW" }
        $finding = if ($issues.Count -eq 0) { "None" } else { $issues -join " " }

        if ($status -eq "PASS") {
            Write-Host "[PASS]   $($script.Name)" -ForegroundColor Green
        }
        else {
            Write-Host "[REVIEW] $($script.Name)" -ForegroundColor Yellow
            foreach ($issue in $issues) {
                Write-Host "         - $issue" -ForegroundColor DarkYellow
            }
        }

        [pscustomobject]@{
            Name = $script.Name
            Status = $status
            Issues = $finding
        }
    }

    $reviewCount = @($results | Where-Object Status -eq "REVIEW").Count
    $passCount = @($results | Where-Object Status -eq "PASS").Count

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# Root PowerShell Path Audit")
    $lines.Add("")
    $lines.Add("- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
    $lines.Add("- Project: $root")
    $lines.Add("- Scripts checked: $($results.Count)")
    $lines.Add("- Passed: $passCount")
    $lines.Add("- Requiring review: $reviewCount")
    $lines.Add("")
    $lines.Add("| Script | Status | Findings |")
    $lines.Add("|---|---|---|")

    foreach ($result in $results) {
        $lines.Add("| $($result.Name) | $($result.Status) | $($result.Issues.Replace('|','\|')) |")
    }

    Set-Content -LiteralPath $reportPath -Value $lines -Encoding UTF8
    Copy-Item -LiteralPath $reportPath -Destination $historyReportPath -Force

    Add-Log -Context $context -Message (
        "Checked {0} root scripts; {1} passed and {2} require review." -f `
        $results.Count, $passCount, $reviewCount
    )

    $status = if ($reviewCount -eq 0) { "SUCCESS" } else { "REVIEW" }
    Complete-Output -Context $context -Status $status -Message "Root PowerShell path audit completed."

    Write-Host ""
    Write-Host "==============================================" -ForegroundColor Cyan
    Write-Host " Root PowerShell Validation Completed" -ForegroundColor Green
    Write-Host "==============================================" -ForegroundColor Cyan
    Write-Host "Passed : $passCount" -ForegroundColor Green
    Write-Host "Review : $reviewCount" -ForegroundColor Yellow
    Write-Host "Report : $reportPath" -ForegroundColor Cyan

    if ($reviewCount -gt 0) { $exitCode = 2 }
}
catch {
    $exitCode = 1
    $message = $_.Exception.Message

    Write-Host ""
    Write-Host "[ERROR] $message" -ForegroundColor Red

    if ($null -ne $context) {
        try { Add-Log -Context $context -Message "[ERROR] $message" } catch { }
        try { Complete-Output -Context $context -Status "FAILED" -Message $message } catch { }
    }
    else {
        try {
            $root = Resolve-ProjectRoot -RequestedPath $ProjectPath
            $latest = Join-Path $root "Output\Latest"
            New-Item -ItemType Directory -Path $latest -Force | Out-Null
            $errorReport = Join-Path $latest "Root-PowerShell-Path-Validation-Error.txt"

            @(
                "Root PowerShell validation failed."
                "Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
                "Error: $message"
                ""
                $_.ScriptStackTrace
            ) | Set-Content -LiteralPath $errorReport -Encoding UTF8

            Write-Host "Error report: $errorReport" -ForegroundColor Yellow
            try { Start-Process explorer.exe -ArgumentList ('"{0}"' -f $latest) | Out-Null } catch { }
        }
        catch { }
    }
}
finally {
    Wait-BeforeExit
}

exit $exitCode
