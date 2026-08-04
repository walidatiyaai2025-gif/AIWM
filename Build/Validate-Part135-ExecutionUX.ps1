param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$file = Join-Path $root "src\AIWordPressManager.Desktop\ViewModels\ExecutionCenterViewModel.cs"

if (-not (Test-Path $file)) { throw "ExecutionCenterViewModel.cs was not found." }
$text = Get-Content $file -Raw
$required = @(
    "UiOperationService _operations",
    "Loading execution center",
    "Approving selected changes",
    "Preparing executable values",
    "Executing safe WordPress changes",
    "_operations.Report"
)
foreach ($item in $required) {
    if (-not $text.Contains($item)) { throw "Missing Part 135 execution UX contract: $item" }
}

Write-Host "Part 135 execution UX contracts passed." -ForegroundColor Green
