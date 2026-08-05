[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [string]$DocumentPath,
    [string]$StatusPath,
    [string]$MarkdownPath,
    [ValidateRange(1, 1000)]
    [int]$BatchSize = 100,
    [switch]$OpenReport,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-CleanPath {
    param([AllowNull()][AllowEmptyString()][string]$Path, [string]$Fallback)

    $value = if ([string]::IsNullOrWhiteSpace($Path)) { $Fallback } else { $Path }
    if ([string]::IsNullOrWhiteSpace($value)) { throw 'A required path could not be resolved.' }

    $clean = $value.Trim().Replace('"', '').Replace("'", '')
    return [System.IO.Path]::GetFullPath($clean)
}

function Wait-BeforeExit {
    if ($NoPause) { return }
    Write-Host ''
    Write-Host '============================================================' -ForegroundColor DarkCyan
    Write-Host 'Press ENTER to close this window...' -ForegroundColor Magenta
    Write-Host '============================================================' -ForegroundColor DarkCyan
    try { [void](Read-Host) } catch { }
}

function Get-CellText {
    param([Parameter(Mandatory)]$Cell, [Parameter(Mandatory)]$NamespaceManager)

    $parts = @($Cell.SelectNodes('.//w:t', $NamespaceManager) | ForEach-Object { $_.'#text' })
    return (($parts -join ' ') -replace '\s+', ' ').Trim()
}

$exitCode = 0
$tempDirectory = $null

try {
    $defaultRoot = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        Split-Path -Parent $PSScriptRoot
    } else {
        (Get-Location).ProviderPath
    }

    $ProjectRoot = Resolve-CleanPath -Path $ProjectRoot -Fallback $defaultRoot
    $DocumentPath = Resolve-CleanPath -Path $DocumentPath -Fallback (Join-Path $ProjectRoot 'AI_WordPress_Manager_Full_Execution_Task_Tracker_AR.docx')
    $StatusPath = Resolve-CleanPath -Path $StatusPath -Fallback (Join-Path $ProjectRoot 'docs\task-tracker\TaskTracker.json')
    $MarkdownPath = Resolve-CleanPath -Path $MarkdownPath -Fallback (Join-Path $ProjectRoot 'docs\task-tracker\TaskTracker.md')

    if (-not (Test-Path -LiteralPath $DocumentPath -PathType Leaf)) {
        throw "Source Word document was not found: $DocumentPath"
    }

    $statusDirectory = Split-Path -Parent $StatusPath
    $markdownDirectory = Split-Path -Parent $MarkdownPath
    New-Item -ItemType Directory -Path $statusDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $markdownDirectory -Force | Out-Null

    $existingById = @{}
    if (Test-Path -LiteralPath $StatusPath -PathType Leaf) {
        $existing = Get-Content -LiteralPath $StatusPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($task in @($existing.tasks)) {
            $existingById[[int]$task.id] = $task
        }
    }

    $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("aiwm-task-import-{0}" -f [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempDirectory -Force | Out-Null

    $zipPath = Join-Path $tempDirectory 'tracker.zip'
    Copy-Item -LiteralPath $DocumentPath -Destination $zipPath -Force
    Expand-Archive -LiteralPath $zipPath -DestinationPath $tempDirectory -Force

    $documentXmlPath = Join-Path $tempDirectory 'word\document.xml'
    if (-not (Test-Path -LiteralPath $documentXmlPath -PathType Leaf)) {
        throw 'word/document.xml was not found inside the DOCX package.'
    }

    [xml]$xml = Get-Content -LiteralPath $documentXmlPath -Raw -Encoding UTF8
    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace('w', 'http://schemas.openxmlformats.org/wordprocessingml/2006/main')

    $importedById = [ordered]@{}
    $duplicateIds = [System.Collections.Generic.List[int]]::new()

    foreach ($row in @($xml.SelectNodes('//w:tbl/w:tr', $ns))) {
        $cells = @($row.SelectNodes('./w:tc', $ns))
        if ($cells.Count -lt 2) { continue }

        $cellTexts = @($cells | ForEach-Object { Get-CellText -Cell $_ -NamespaceManager $ns })
        $idMatch = [regex]::Match($cellTexts[0], '^\s*(\d{1,4})\s*$')
        if (-not $idMatch.Success) { continue }

        $id = [int]$idMatch.Groups[1].Value
        $candidateTexts = @($cellTexts | Select-Object -Skip 1 | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_) -and
            $_ -notmatch '^(مكتملة|غير مكتملة|منفذة بالفعل|Completed|Pending)$'
        })

        if ($candidateTexts.Count -eq 0) { continue }
        $title = $candidateTexts[0].Trim()

        if ($importedById.Contains($id)) {
            $duplicateIds.Add($id)
            continue
        }

        $old = $existingById[$id]
        $status = if ($null -ne $old -and -not [string]::IsNullOrWhiteSpace([string]$old.status)) {
            [string]$old.status
        } else {
            'pending'
        }
        $note = if ($null -ne $old -and -not [string]::IsNullOrWhiteSpace([string]$old.note)) {
            [string]$old.note
        } else {
            ''
        }

        $importedById[$id] = [ordered]@{
            id = $id
            title = $title
            status = $status
            note = $note
        }
    }

    if ($importedById.Count -eq 0) {
        throw 'No numbered task rows were detected in the Word document.'
    }

    $tasks = @($importedById.Values | Sort-Object { [int]$_.id })
    $ids = @($tasks | ForEach-Object { [int]$_.id })
    $maximumId = ($ids | Measure-Object -Maximum).Maximum
    $missingIds = @(1..$maximumId | Where-Object { $_ -notin $ids })
    $completedCount = @($tasks | Where-Object { $_.status -in @('completed', 'already-completed') }).Count
    $pendingCount = $tasks.Count - $completedCount
    $firstPending = @($tasks | Where-Object { $_.status -notin @('completed', 'already-completed') } | Select-Object -First 1)
    $currentBatch = if ($firstPending.Count -eq 0) {
        [Math]::Ceiling($tasks.Count / [double]$BatchSize)
    } else {
        [Math]::Floor(([int]$firstPending[0].id - 1) / $BatchSize) + 1
    }

    $tracker = [ordered]@{
        sourceDocument = [System.IO.Path]::GetFileName($DocumentPath)
        totalTasks = $tasks.Count
        batchSize = $BatchSize
        currentBatch = [int]$currentBatch
        lastUpdatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        tasks = $tasks
        validation = [ordered]@{
            maximumTaskId = [int]$maximumId
            missingTaskIds = $missingIds
            duplicateTaskIds = @($duplicateIds | Sort-Object -Unique)
            importedRows = $tasks.Count
        }
    }

    $tracker | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $StatusPath -Encoding UTF8

    $batchFrom = (($currentBatch - 1) * $BatchSize) + 1
    $batchTo = [Math]::Min($currentBatch * $BatchSize, $maximumId)
    $batchTasks = @($tasks | Where-Object { $_.id -ge $batchFrom -and $_.id -le $batchTo })

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# AI WordPress Manager - Task Tracker')
    $lines.Add('')
    $lines.Add("- Last updated (UTC): $($tracker.lastUpdatedUtc)")
    $lines.Add("- Source: `$($tracker.sourceDocument)`")
    $lines.Add("- Total imported tasks: $($tasks.Count)")
    $lines.Add("- Completed: $completedCount")
    $lines.Add("- Pending: $pendingCount")
    $lines.Add("- Current batch: $currentBatch ($batchFrom-$batchTo)")
    $lines.Add("- Missing IDs: $(if ($missingIds.Count) { $missingIds -join ', ' } else { 'None' })")
    $lines.Add("- Duplicate IDs: $(if ($duplicateIds.Count) { (@($duplicateIds | Sort-Object -Unique) -join ', ') } else { 'None' })")
    $lines.Add('')
    $lines.Add("## Current batch: $batchFrom-$batchTo")
    $lines.Add('')
    $lines.Add('| ID | Status | Task | Note |')
    $lines.Add('|---:|---|---|---|')

    foreach ($task in $batchTasks) {
        $statusIcon = if ($task.status -in @('completed', 'already-completed')) { '✅' } else { '⬜' }
        $safeTitle = ([string]$task.title).Replace('|', '\|')
        $safeNote = ([string]$task.note).Replace('|', '\|')
        $lines.Add("| $($task.id) | $statusIcon $($task.status) | $safeTitle | $safeNote |")
    }

    $lines.Add('')
    $lines.Add('## Validation')
    $lines.Add('')
    $lines.Add('- The importer preserves existing task status and notes by task ID.')
    $lines.Add('- Task titles are read directly from the Word table and are not generated or rewritten.')
    $lines.Add('- Missing or duplicate IDs must be resolved before a batch is declared complete.')

    Set-Content -LiteralPath $MarkdownPath -Value $lines -Encoding UTF8

    Write-Host ''
    Write-Host 'Task tracker import completed.' -ForegroundColor Green
    Write-Host "Imported tasks : $($tasks.Count)" -ForegroundColor Cyan
    Write-Host "Completed      : $completedCount" -ForegroundColor Green
    Write-Host "Pending        : $pendingCount" -ForegroundColor Yellow
    Write-Host "Current batch  : $currentBatch ($batchFrom-$batchTo)" -ForegroundColor Cyan
    Write-Host "JSON           : $StatusPath" -ForegroundColor DarkGray
    Write-Host "Markdown       : $MarkdownPath" -ForegroundColor DarkGray

    if ($missingIds.Count -gt 0 -or $duplicateIds.Count -gt 0) {
        Write-Warning 'Task ID validation found missing or duplicate IDs. Review TaskTracker.md.'
        $exitCode = 2
    }

    if ($OpenReport) {
        Start-Process explorer.exe -ArgumentList "/select,`"$MarkdownPath`"" | Out-Null
    }
}
catch {
    $exitCode = 1
    Write-Host ''
    Write-Host "[ERROR] $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
}
finally {
    if ($tempDirectory -and (Test-Path -LiteralPath $tempDirectory)) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
    Wait-BeforeExit
}

exit $exitCode
