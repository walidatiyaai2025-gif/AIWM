Set-StrictMode -Version Latest

function Resolve-AiwmSafePath {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Path,
        [AllowNull()]
        [AllowEmptyString()]
        [string]$FallbackPath
    )

    $candidates = @($Path, $FallbackPath, $PSScriptRoot, (Get-Location).ProviderPath)
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

    throw "Could not resolve a valid project path."
}

function Initialize-AiwmScriptOutput {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [AllowEmptyString()]
        [string]$ProjectRoot,
        [Parameter(Mandatory)]
        [string]$ScriptName,
        [AllowNull()]
        [AllowEmptyString()]
        [string]$FallbackRoot
    )

    $root = Resolve-AiwmSafePath -Path $ProjectRoot -FallbackPath $FallbackRoot
    $outputRoot = Join-Path $root "Output"
    $latest = Join-Path $outputRoot "Latest"
    $historyRoot = Join-Path $outputRoot "History"
    $history = Join-Path $historyRoot (Get-Date -Format "yyyy-MM-dd_HH-mm-ss")

    foreach ($directory in @($outputRoot, $latest, $historyRoot, $history)) {
        New-Item -ItemType Directory -LiteralPath $directory -Force | Out-Null
    }

    $safeName = [System.IO.Path]::GetFileNameWithoutExtension($ScriptName)
    if ([string]::IsNullOrWhiteSpace($safeName)) { $safeName = "script" }
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

    if ($null -eq $Context) { return }
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

function Wait-AiwmBeforeExit {
    [CmdletBinding()]
    param(
        [switch]$NoPause,
        [string]$Message = "Press ENTER to close this window..."
    )

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
