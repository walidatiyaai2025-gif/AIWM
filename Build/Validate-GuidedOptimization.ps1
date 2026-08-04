param([string]$Configuration = "Debug")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$vm = Join-Path $root "src/AIWordPressManager.Desktop/ViewModels/MainWindowViewModel.cs"
$xaml = Join-Path $root "src/AIWordPressManager.Desktop/MainWindow.xaml"

foreach ($file in @($vm, $xaml)) {
    if (-not (Test-Path $file)) { throw "Required file not found: $file" }
}

$vmText = Get-Content $vm -Raw
$xamlText = Get-Content $xaml -Raw
$required = @(
    "StartOptimizationCommand",
    "StartOptimizationAsync",
    "SeoAudit.RunAuditCommand",
    "ContentAudit.RunAuditCommand",
    "BrokenLinks.RunScanCommand",
    "CategoryPlanner.AnalyzeCommand",
    "SuggestedChanges.GenerateCommand"
)
foreach ($token in $required) {
    if (-not $vmText.Contains($token)) { throw "Guided workflow contract missing: $token" }
}
if (-not $xamlText.Contains('Command="{Binding StartOptimizationCommand}"')) {
    throw "Dashboard Start optimization is not bound to StartOptimizationCommand."
}
Write-Host "Guided optimization contracts passed." -ForegroundColor Green

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    dotnet restore (Join-Path $root "AIWordPressManager.sln")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet build (Join-Path $root "AIWordPressManager.sln") -c $Configuration --no-restore
    exit $LASTEXITCODE
}
Write-Warning ".NET SDK is unavailable; source contracts passed but build was not run."
