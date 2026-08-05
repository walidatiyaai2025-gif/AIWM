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

    try {
        [void](Read-Host)
    }
    catch {
        try { [void]$Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") }
        catch { Start-Sleep -Seconds 15 }
    }
}

function Resolve-ExistingProjectRoot {
    param([AllowNull()][AllowEmptyString()][string]$RequestedPath)

    $candidates = [System.Collections.Generic.List[string]]::new()

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates.Add($RequestedPath)
    }

    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        $candidates.Add($PSScriptRoot)
    }

    $scriptFile = $MyInvocation.ScriptName
    if (-not [string]::IsNullOrWhiteSpace($scriptFile)) {
        $scriptDirectory = Split-Path -Parent $scriptFile
        if (-not [string]::IsNullOrWhiteSpace($scriptDirectory)) {
            $candidates.Add($scriptDirectory)
        }
    }

    $currentDirectory = (Get-Location).ProviderPath
    if (-not [string]::IsNullOrWhiteSpace($currentDirectory)) {
        $candidates.Add($currentDirectory)
    }

    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }

        $clean = $candidate.Trim().Trim('"').Trim("'")
        if ([string]::IsNullOrWhiteSpace($clean)) { continue }

        try {
            $full = [System.IO.Path]::GetFullPath($clean)
        }
        catch {
            continue
        }

        if (Test-Path -LiteralPath $full -PathType Container) {
            return $full
        }
    }

    throw "Could not resolve the project root from ProjectPath, PSScriptRoot, script location, or current directory."
}

$exitCode = 0
$context = $null
$latestOutput = $null
$reportPath = $null

try {
    $resolvedRoot = Resolve-ExistingProjectRoot -RequestedPath $ProjectPath

    $commonPath = Join-Path $resolvedRoot "Build\RootScript.Common.ps1"
    if (-not (Test-Path -LiteralPath $commonPath -PathType Leaf)) {
        throw "Shared root-script helper was not found: $commonPath"
    }

    . $commonPath

    $context = Initialize-AiwmScriptOutput `
        -ProjectRoot $resolvedRoot `
        -ScriptName "Validate-Root-PowerShell-Paths.ps1"

    $latestOutput = $context.LatestOutput
    $reportPath = Join-Path $latestOutput "Root-PowerShell-Path-Audit.md"
    $historyReportPath = Join-Path $context.HistoryOutput "Root-PowerShell-Path-Audit.md"

    Write-Host ""
    Write-Host "Validating root PowerShell scripts..." -ForegroundColor Cyan
    Write-Host "Project: $($context.ProjectRoot)" -ForegroundColor DarkGray
    Write-Host ""

    $scripts = @(
        Get-ChildItem `
            -LiteralPath $context.ProjectRoot `
            -File `
            -Filter "*.ps1" `
            -ErrorAction Stop |
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
        if ($content -match 'Add-Content\s+-Path\s+\$LogPath' -and
            $content -notmatch 'Resolve-SafePath|Resolve-AiwmSafePath|Initialize-AiwmScriptOutput') {
            $issues.Add("Log path is written without explicit path sanitization.")
        }
        if ($content -match '\$LogPath\s*=\s*"\$[^\r\n]+\\') {
            $issues.Add("Log path is built with string interpolation instead of Join-Path.")
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
    $lines.Add("- Project: $($context.ProjectRoot)")
    $lines.Add("- Scripts checked: $($results.Count)")
    $lines.Add("- Passed: $passCount")
    $lines.Add("- Requiring review: $reviewCount")
    $lines.Add("")
    $lines.Add("| Script | Status | Findings |")
    $lines.Add("|---|---|---|")

    foreach ($result in $results) {
        $findings = $result.Issues.Replace("|", "\|")
        $lines.Add("| $($result.Name) | $($result.Status) | $findings |")
    }

    Set-Content -LiteralPath $reportPath -Value $lines -Encoding UTF8
    Copy-Item -LiteralPath $reportPath -Destination $historyReportPath -Force

    Add-AiwmLog -Context $context -Message (
        "Checked {0} root scripts; {1} passed and {2} require review." -f `
        $results.Count, $passCount, $reviewCount
    )

    $status = if ($reviewCount -eq 0) { "SUCCESS" } else { "REVIEW" }
    Complete-AiwmScriptOutput `
        -Context $context `
        -Status $status `
        -Message "Root PowerShell path audit completed."

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
    $errorMessage = $_.Exception.Message

    Write-Host ""
    Write-Host "[ERROR] $errorMessage" -ForegroundColor Red

    if ($null -ne $context) {
        try { Add-AiwmLog -Context $context -Message "[ERROR] $errorMessage" } catch { }
        try {
            Complete-AiwmScriptOutput `
                -Context $context `
                -Status "FAILED" `
                -Message $errorMessage
        }
        catch { }
    }
    else {
        try {
            $fallbackRoot = Resolve-ExistingProjectRoot -RequestedPath $ProjectPath
            $latestOutput = Join-Path $fallbackRoot "Output\Latest"
            New-Item -ItemType Directory -Path $latestOutput -Force | Out-Null
            $errorReport = Join-Path $latestOutput "Root-PowerShell-Path-Validation-Error.txt"
            @(
                "Root PowerShell validation failed."
                "Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
                "Error: $errorMessage"
                ""
                $_.ScriptStackTrace
            ) | Set-Content -LiteralPath $errorReport -Encoding UTF8
            Write-Host "Error report: $errorReport" -ForegroundColor Yellow
            try { Start-Process explorer.exe -ArgumentList ('"{0}"' -f $latestOutput) | Out-Null } catch { }
        }
        catch { }
    }
}
finally {
    Wait-BeforeExit
}

exit $exitCode
