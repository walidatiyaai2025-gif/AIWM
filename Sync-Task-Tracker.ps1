[CmdletBinding()]
param(
    [switch]$SkipWordColoring,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    [System.IO.Path]::GetFullPath($PSScriptRoot)
} else {
    [System.IO.Path]::GetFullPath((Get-Location).ProviderPath)
}

$importScript = Join-Path $root 'Build\Import-TaskTrackerFromDocx.ps1'
$wordUpdateScript = Join-Path $root 'Build\Update-TaskTrackerDocx.ps1'
$reportPath = Join-Path $root 'docs\task-tracker\TaskTracker.md'
$jsonPath = Join-Path $root 'docs\task-tracker\TaskTracker.json'
$documentPath = Join-Path $root 'AI_WordPress_Manager_Full_Execution_Task_Tracker_AR.docx'
$exitCode = 0

function Wait-BeforeExit {
    if ($NoPause) { return }
    Write-Host ''
    Write-Host '============================================================' -ForegroundColor DarkCyan
    Write-Host 'Press ENTER to close this window...' -ForegroundColor Magenta
    Write-Host '============================================================' -ForegroundColor DarkCyan
    try { [void](Read-Host) } catch { }
}

function Invoke-PowerShellScript {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string[]]$Arguments = @(),
        [int[]]$AllowedExitCodes = @(0)
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required script was not found: $Path"
    }

    Write-Host ''
    Write-Host "==> Running $([System.IO.Path]::GetFileName($Path))" -ForegroundColor Cyan
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments
    $code = $LASTEXITCODE
    if ($code -notin $AllowedExitCodes) {
        throw "$([System.IO.Path]::GetFileName($Path)) failed with exit code $code."
    }
    return $code
}

try {
    if (-not (Test-Path -LiteralPath $documentPath -PathType Leaf)) {
        throw "Task tracker Word document was not found: $documentPath"
    }

    $importCode = Invoke-PowerShellScript `
        -Path $importScript `
        -Arguments @('-ProjectRoot', $root, '-NoPause') `
        -AllowedExitCodes @(0, 2)

    if ($importCode -eq 2) {
        Write-Warning 'The task list was imported, but missing or duplicate IDs were detected.'
    }

    if (-not $SkipWordColoring) {
        try {
            Invoke-PowerShellScript `
                -Path $wordUpdateScript `
                -Arguments @(
                    '-ProjectRoot', $root,
                    '-DocumentPath', $documentPath,
                    '-StatusPath', $jsonPath
                ) | Out-Null
        }
        catch {
            Write-Warning "Word coloring was skipped: $($_.Exception.Message)"
        }
    }

    if (Test-Path -LiteralPath $reportPath -PathType Leaf) {
        Write-Host ''
        Write-Host 'Task tracker synchronized successfully.' -ForegroundColor Green
        Write-Host "Report: $reportPath" -ForegroundColor Cyan
        try { Start-Process explorer.exe -ArgumentList "/select,`"$reportPath`"" | Out-Null } catch { }
    }
    else {
        throw "Task tracker report was not generated: $reportPath"
    }
}
catch {
    $exitCode = 1
    Write-Host ''
    Write-Host "[ERROR] $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    Wait-BeforeExit
}

exit $exitCode
