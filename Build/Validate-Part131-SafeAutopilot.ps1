param([string]$Configuration = "Debug")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$vm = Join-Path $root "src\AIWordPressManager.Desktop\ViewModels\MainWindowViewModel.cs"
$xaml = Join-Path $root "src\AIWordPressManager.Desktop\MainWindow.xaml"

foreach ($path in @($vm, $xaml)) {
    if (-not (Test-Path $path)) { throw "Required file missing: $path" }
}

$vmText = Get-Content $vm -Raw
$xamlText = Get-Content $xaml -Raw
$required = @(
    "RunSafeAutopilotCommand",
    "RunSafeAutopilotAsync",
    "WriteOptimizationReceiptAsync",
    "SafeAutopilotReadiness",
    "OpenLastOptimizationReceiptCommand"
)
foreach ($token in $required) {
    if ($vmText -notmatch [regex]::Escape($token) -and $xamlText -notmatch [regex]::Escape($token)) {
        throw "Part 131 contract missing: $token"
    }
}

[xml](Get-Content $xaml -Raw) | Out-Null
Write-Host "Part 131 static validation passed." -ForegroundColor Green

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    dotnet restore (Join-Path $root "AIWordPressManager.sln")
    dotnet build (Join-Path $root "AIWordPressManager.sln") -c $Configuration --no-restore
} else {
    Write-Warning ".NET SDK not found; build step skipped."
}
