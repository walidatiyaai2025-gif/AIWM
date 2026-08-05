[CmdletBinding()]
param(
    [string]$ProjectPath = $PSScriptRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$commonPath = Join-Path $PSScriptRoot "Build\RootScript.Common.ps1"
if (-not (Test-Path -LiteralPath $commonPath)) {
    throw "Shared root-script helper was not found: $commonPath"
}
. $commonPath

$context = Initialize-AiwmScriptOutput -ProjectRoot $ProjectPath -ScriptName $MyInvocation.MyCommand.Name
$reportPath = Join-Path $context.LatestOutput "Root-PowerShell-Path-Audit.md"
$historyReportPath = Join-Path $context.HistoryOutput "Root-PowerShell-Path-Audit.md"

try {
    $scripts = Get-ChildItem -LiteralPath $context.ProjectRoot -File -Filter "*.ps1" |
        Sort-Object Name

    $results = foreach ($script in $scripts) {
        $content = Get-Content -LiteralPath $script.FullName -Raw
        $issues = [System.Collections.Generic.List[string]]::new()

        if ($content -match '\$PSScriptRoot\s*\+') {
            $issues.Add("Manual concatenation with `$PSScriptRoot; use Join-Path.")
        }
        if ($content -match '\$ProjectPath\s*\+') {
            $issues.Add("Manual concatenation with `$ProjectPath; use Join-Path.")
        }
        if ($content -match 'Add-Content\s+-Path\s+\$LogPath' -and $content -notmatch 'Resolve-SafePath|Resolve-AiwmSafePath') {
            $issues.Add("Log path is written without explicit path sanitization.")
        }
        if ($content -match '\$LogPath\s*=\s*"\$[^\r\n]+\\') {
            $issues.Add("Log path is built with string interpolation instead of Join-Path.")
        }
        if ($content -match '\.Trim\(\)\s*$' -and $content -notmatch "Trim\('\"'\)") {
            $issues.Add("Input paths may retain quote characters.")
        }

        [pscustomobject]@{
            Name = $script.Name
            Status = if ($issues.Count -eq 0) { "PASS" } else { "REVIEW" }
            Issues = if ($issues.Count -eq 0) { "None" } else { $issues -join " " }
        }
    }

    $reviewCount = @($results | Where-Object Status -eq "REVIEW").Count
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# Root PowerShell Path Audit")
    $lines.Add("")
    $lines.Add("- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
    $lines.Add("- Project: $($context.ProjectRoot)")
    $lines.Add("- Scripts checked: $($results.Count)")
    $lines.Add("- Scripts requiring review: $reviewCount")
    $lines.Add("")
    $lines.Add("| Script | Status | Findings |")
    $lines.Add("|---|---|---|")

    foreach ($result in $results) {
        $findings = $result.Issues.Replace("|", "\|")
        $lines.Add("| $($result.Name) | $($result.Status) | $findings |")
    }

    Set-Content -LiteralPath $reportPath -Value $lines -Encoding UTF8
    Copy-Item -LiteralPath $reportPath -Destination $historyReportPath -Force
    Add-AiwmLog -Context $context -Message "Checked $($results.Count) root PowerShell scripts; $reviewCount require review."

    $status = if ($reviewCount -eq 0) { "SUCCESS" } else { "REVIEW" }
    Complete-AiwmScriptOutput -Context $context -Status $status -Message "Root PowerShell path audit completed."

    if ($reviewCount -gt 0) {
        Write-Warning "$reviewCount root scripts require path review. See $reportPath"
        exit 2
    }
}
catch {
    $message = "[ERROR] $($_.Exception.Message)"
    try { Add-AiwmLog -Context $context -Message $message } catch { }
    Complete-AiwmScriptOutput -Context $context -Status "FAILED" -Message $message
    throw
}
