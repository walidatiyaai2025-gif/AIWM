[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")),
    [string]$DocumentPath,
    [string]$TrackerPath,
    [string]$MarkdownPath,
    [int]$BatchSize = 100,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Wait-BeforeExit {
    if ($NoPause) { return }
    Write-Host ""
    Write-Host "Press ENTER to close..." -ForegroundColor Magenta
    try { [void](Read-Host) } catch { }
}

function Get-CellText {
    param(
        [Parameter(Mandatory)]$Cell,
        [Parameter(Mandatory)][System.Xml.XmlNamespaceManager]$Ns
    )

    $parts = @($Cell.SelectNodes('.//w:t', $Ns) | ForEach-Object { $_.'#text' })
    return (($parts -join ' ') -replace '\s+', ' ').Trim()
}

try {
    $ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot.Trim().Trim('"').Trim("'"))

    if (-not $DocumentPath) {
        $DocumentPath = Join-Path $ProjectRoot 'AI_WordPress_Manager_Full_Execution_Task_Tracker_AR.docx'
    }
    if (-not $TrackerPath) {
        $TrackerPath = Join-Path $ProjectRoot 'docs\task-tracker\TaskTracker.json'
    }
    if (-not $MarkdownPath) {
        $MarkdownPath = Join-Path $ProjectRoot 'docs\task-tracker\TaskTracker.md'
    }

    foreach ($path in @($DocumentPath, $TrackerPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required file not found: $path"
        }
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("aiwm-task-import-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    try {
        [System.IO.Compression.ZipFile]::ExtractToDirectory($DocumentPath, $tempRoot)
        $documentXmlPath = Join-Path $tempRoot 'word\document.xml'
        if (-not (Test-Path -LiteralPath $documentXmlPath -PathType Leaf)) {
            throw 'word/document.xml was not found inside the DOCX package.'
        }

        [xml]$xml = Get-Content -LiteralPath $documentXmlPath -Raw -Encoding UTF8
        $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
        $ns.AddNamespace('w', 'http://schemas.openxmlformats.org/wordprocessingml/2006/main')

        $imported = [System.Collections.Generic.Dictionary[int, string]]::new()
        $duplicates = [System.Collections.Generic.List[int]]::new()

        foreach ($row in @($xml.SelectNodes('//w:tbl/w:tr', $ns))) {
            $cells = @($row.SelectNodes('./w:tc', $ns))
            if ($cells.Count -lt 2) { continue }

            $first = Get-CellText -Cell $cells[0] -Ns $ns
            $idMatch = [regex]::Match($first, '(?<!\d)(\d{1,3})(?!\d)')
            if (-not $idMatch.Success) { continue }

            $id = [int]$idMatch.Groups[1].Value
            if ($id -lt 1 -or $id -gt 10000) { continue }

            $title = Get-CellText -Cell $cells[1] -Ns $ns
            if ([string]::IsNullOrWhiteSpace($title)) { continue }

            if ($imported.ContainsKey($id)) {
                $duplicates.Add($id)
                continue
            }
            $imported.Add($id, $title)
        }

        if ($imported.Count -eq 0) {
            throw 'No numbered task rows were detected in the Word document.'
        }

        $tracker = Get-Content -LiteralPath $TrackerPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $existing = @{}
        foreach ($task in @($tracker.tasks)) { $existing[[int]$task.id] = $task }

        $maxId = ($imported.Keys | Measure-Object -Maximum).Maximum
        $tasks = [System.Collections.Generic.List[object]]::new()

        foreach ($id in ($imported.Keys | Sort-Object)) {
            $old = $existing[$id]
            $status = if ($old -and $old.status) { [string]$old.status } else { 'pending' }
            $note = if ($old -and $old.note) { [string]$old.note } else { '' }

            $tasks.Add([ordered]@{
                id = $id
                title = $imported[$id]
                status = $status
                note = $note
            })
        }

        $firstPending = @($tasks | Where-Object { $_.status -notin @('completed', 'already-completed') } | Select-Object -First 1)
        $currentBatch = if ($firstPending.Count -eq 0) {
            [math]::Ceiling($maxId / [double]$BatchSize)
        } else {
            [math]::Floor(([int]$firstPending[0].id - 1) / $BatchSize) + 1
        }

        $missing = @()
        for ($id = 1; $id -le $maxId; $id++) {
            if (-not $imported.ContainsKey($id)) { $missing += $id }
        }

        $output = [ordered]@{
            sourceDocument = [System.IO.Path]::GetFileName($DocumentPath)
            totalTasks = $tasks.Count
            batchSize = $BatchSize
            currentBatch = $currentBatch
            lastUpdatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
            tasks = $tasks
            validation = [ordered]@{
                maximumTaskId = $maxId
                missingTaskIds = $missing
                duplicateTaskIds = @($duplicates | Sort-Object -Unique)
            }
        }

        $output | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $TrackerPath -Encoding UTF8

        $batchStart = (($currentBatch - 1) * $BatchSize) + 1
        $batchEnd = [math]::Min($currentBatch * $BatchSize, $maxId)
        $batchTasks = @($tasks | Where-Object { $_.id -ge $batchStart -and $_.id -le $batchEnd })
        $completed = @($tasks | Where-Object { $_.status -in @('completed', 'already-completed') }).Count
        $pending = $tasks.Count - $completed

        $md = [System.Collections.Generic.List[string]]::new()
        $md.Add('# AI WordPress Manager — Execution Task Tracker')
        $md.Add('')
        $md.Add("- إجمالي المهام المستوردة: **$($tasks.Count)**")
        $md.Add("- حجم الدفعة: **$BatchSize**")
        $md.Add("- الدفعة الحالية: **$currentBatch ($batchStart–$batchEnd)**")
        $md.Add("- مكتملة: **$completed**")
        $md.Add("- غير مكتملة: **$pending**")
        $md.Add('')
        $md.Add("## مهام الدفعة الحالية ($batchStart–$batchEnd)")
        $md.Add('')
        $md.Add('| رقم | المهمة | الحالة | الملاحظة |')
        $md.Add('|---:|---|---|---|')
        foreach ($task in $batchTasks) {
            $state = if ($task.status -in @('completed', 'already-completed')) { '✅ مكتملة' } else { '🩷 غير مكتملة' }
            $safeTitle = ([string]$task.title).Replace('|', '\|')
            $safeNote = ([string]$task.note).Replace('|', '\|')
            $md.Add("| $($task.id) | $safeTitle | $state | $safeNote |")
        }

        if ($missing.Count -gt 0 -or $duplicates.Count -gt 0) {
            $md.Add('')
            $md.Add('## نتائج التحقق')
            $md.Add('')
            $md.Add("- أرقام مفقودة: $($missing -join ', ')")
            $md.Add("- أرقام مكررة: $(@($duplicates | Sort-Object -Unique) -join ', ')")
        }

        $md | Set-Content -LiteralPath $MarkdownPath -Encoding UTF8

        Write-Host "Imported $($tasks.Count) tasks from DOCX XML." -ForegroundColor Green
        Write-Host "Current batch: $currentBatch ($batchStart-$batchEnd)" -ForegroundColor Cyan
        Write-Host "Completed: $completed | Pending: $pending" -ForegroundColor Cyan
        if ($missing.Count -gt 0) { Write-Warning "Missing IDs: $($missing -join ', ')" }
        if ($duplicates.Count -gt 0) { Write-Warning "Duplicate IDs: $(@($duplicates | Sort-Object -Unique) -join ', ')" }
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
catch {
    Write-Host "[ERROR] $($_.Exception.Message)" -ForegroundColor Red
    throw
}
finally {
    Wait-BeforeExit
}
