[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")),
    [string]$DocumentPath,
    [string]$StatusPath,
    [switch]$NoBackup
)

$ErrorActionPreference = "Stop"

if (-not $DocumentPath) {
    $DocumentPath = Join-Path $ProjectRoot "AI_WordPress_Manager_Full_Execution_Task_Tracker_AR.docx"
}
if (-not $StatusPath) {
    $StatusPath = Join-Path $ProjectRoot "docs\task-tracker\TaskTracker.json"
}

if (-not (Test-Path $DocumentPath)) {
    throw "Task tracker document was not found: $DocumentPath"
}
if (-not (Test-Path $StatusPath)) {
    throw "Task tracker status file was not found: $StatusPath"
}

$status = Get-Content $StatusPath -Raw -Encoding UTF8 | ConvertFrom-Json
$taskById = @{}
foreach ($task in $status.tasks) {
    $taskById[[int]$task.id] = $task
}

function Convert-HexToWordColor([string]$Hex) {
    $hexValue = $Hex.TrimStart('#')
    if ($hexValue.Length -ne 6) { throw "Invalid RGB color: $Hex" }
    $r = [Convert]::ToInt32($hexValue.Substring(0, 2), 16)
    $g = [Convert]::ToInt32($hexValue.Substring(2, 2), 16)
    $b = [Convert]::ToInt32($hexValue.Substring(4, 2), 16)
    return $r + ($g * 256) + ($b * 65536)
}

$completedColor = Convert-HexToWordColor "E2F0D9"
$pendingColor = Convert-HexToWordColor "FCE4EC"
$headerColor = Convert-HexToWordColor "D9EAF7"

$backupPath = $null
if (-not $NoBackup) {
    $backupDirectory = Join-Path $ProjectRoot "Backups\TaskTracker"
    New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
    $backupPath = Join-Path $backupDirectory ("AI_WordPress_Manager_Full_Execution_Task_Tracker_AR_{0}.docx" -f (Get-Date -Format "yyyyMMdd_HHmmss"))
    Copy-Item $DocumentPath $backupPath -Force
}

$word = $null
$document = $null
try {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $document = $word.Documents.Open($DocumentPath, $false, $false)

    $updated = 0
    $completed = 0
    $pending = 0

    foreach ($table in @($document.Tables)) {
        if ($table.Rows.Count -lt 2) { continue }

        # Keep header rows visually distinct.
        try { $table.Rows.Item(1).Shading.BackgroundPatternColor = $headerColor } catch { }

        for ($rowIndex = 2; $rowIndex -le $table.Rows.Count; $rowIndex++) {
            $row = $table.Rows.Item($rowIndex)
            $firstCellText = $row.Cells.Item(1).Range.Text -replace '[\r\a]', ''
            $idMatch = [regex]::Match($firstCellText, '\d+')
            if (-not $idMatch.Success) { continue }

            $taskId = [int]$idMatch.Value
            $task = $taskById[$taskId]
            $isCompleted = $task -and $task.status -in @('completed', 'already-completed')
            $row.Shading.BackgroundPatternColor = if ($isCompleted) { $completedColor } else { $pendingColor }

            if ($isCompleted) { $completed++ } else { $pending++ }

            # Locate or append a status cell without changing existing task wording.
            $statusText = if ($task -and $task.status -eq 'already-completed') {
                'منفذة بالفعل قبل بدء التتبع'
            } elseif ($isCompleted) {
                'مكتملة'
            } else {
                'غير مكتملة'
            }

            $targetCell = $null
            if ($row.Cells.Count -ge 4) {
                $targetCell = $row.Cells.Item($row.Cells.Count)
            } else {
                try {
                    $targetCell = $row.Cells.Add()
                } catch {
                    $targetCell = $row.Cells.Item($row.Cells.Count)
                }
            }

            $existing = $targetCell.Range.Text -replace '[\r\a]', ''
            if ([string]::IsNullOrWhiteSpace($existing) -or $existing -match 'مكتمل|غير مكتمل|منفذة بالفعل|Pending|Completed') {
                $targetCell.Range.Text = $statusText
            }
            $updated++
        }
    }

    $document.BuiltInDocumentProperties.Item('Comments').Value = "Task tracker updated from docs/task-tracker/TaskTracker.json at $([DateTimeOffset]::Now.ToString('O'))"
    $document.Save()

    Write-Host "Task tracker Word document updated successfully." -ForegroundColor Green
    Write-Host "Rows updated: $updated | Completed: $completed | Pending: $pending" -ForegroundColor Cyan
    if ($backupPath) { Write-Host "Backup: $backupPath" -ForegroundColor DarkGray }
}
finally {
    if ($document) { $document.Close($false) | Out-Null }
    if ($word) { $word.Quit() | Out-Null }
    if ($document) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($document) }
    if ($word) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($word) }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}
