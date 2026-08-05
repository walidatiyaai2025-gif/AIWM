Set-StrictMode -Version Latest

function Resolve-AiwmSafePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $clean = $Path.Trim().Trim('"').Trim("'")
    foreach ($invalid in [System.IO.Path]::GetInvalidPathChars()) {
        $clean = $clean.Replace([string]$invalid, "")
    }

    if ([string]::IsNullOrWhiteSpace($clean)) {
        throw "Path is empty after sanitization."
    }

    return [System.IO.Path]::GetFullPath($clean)
}

function Initialize-AiwmScriptOutput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$ProjectRoot,
        [Parameter(Mandatory)]
        [string]$ScriptName
    )

    $root = Resolve-AiwmSafePath -Path $ProjectRoot
    $outputRoot = Join-Path $root "Output"
    $latest = Join-Path $outputRoot "Latest"
    $historyRoot = Join-Path $outputRoot "History"
    $history = Join-Path $historyRoot (Get-Date -Format "yyyy-MM-dd_HH-mm-ss")

    foreach ($directory in @($outputRoot, $latest, $historyRoot, $history)) {
        New-Item -ItemType Directory -LiteralPath $directory -Force | Out-Null
    }

    $safeName = [System.IO.Path]::GetFileNameWithoutExtension($ScriptName)
    $logName = "$safeName.log"

    [pscustomobject]@{
        ProjectRoot = $root
        OutputRoot = $outputRoot
        LatestOutput = $latest
        HistoryOutput = $history
        LogPath = Join-Path $latest $logName
        HistoryLogPath = Join-Path $history $logName
        SummaryPath = Join-Path $latest "Summary.txt"
    }
}

function Add-AiwmLog {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][string]$Message
    )

    Add-Content -LiteralPath $Context.LogPath -Value $Message -Encoding UTF8
    Add-Content -LiteralPath $Context.HistoryLogPath -Value $Message -Encoding UTF8
}

function Complete-AiwmScriptOutput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][string]$Status,
        [Parameter(Mandatory)][string]$Message,
        [switch]$DoNotOpen
    )

    $summary = @(
        "AI WordPress Manager",
        "Status: $Status",
        "Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
        "Project: $($Context.ProjectRoot)",
        "Message: $Message",
        "Latest output: $($Context.LatestOutput)",
        "History output: $($Context.HistoryOutput)"
    ) -join [Environment]::NewLine

    Set-Content -LiteralPath $Context.SummaryPath -Value $summary -Encoding UTF8
    Copy-Item -LiteralPath $Context.SummaryPath -Destination (Join-Path $Context.HistoryOutput "Summary.txt") -Force

    if (-not $DoNotOpen) {
        try {
            Start-Process explorer.exe -ArgumentList ('"{0}"' -f $Context.LatestOutput) | Out-Null
        }
        catch {
            Write-Host "Output folder: $($Context.LatestOutput)" -ForegroundColor Yellow
        }
    }
}
