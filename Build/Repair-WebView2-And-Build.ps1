param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'AIWordPressManager.sln'
$desktopProject = Join-Path $root 'src\AIWordPressManager.Desktop\AIWordPressManager.Desktop.csproj'

Write-Host '[1/7] Stopping .NET build servers...' -ForegroundColor Cyan
dotnet build-server shutdown | Out-Host

Write-Host '[2/7] Removing stale bin/obj folders...' -ForegroundColor Cyan
Get-ChildItem -Path $root -Directory -Recurse -Force |
    Where-Object { $_.Name -in @('bin','obj') } |
    Sort-Object FullName -Descending |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host '[3/7] Clearing NuGet HTTP/temp caches...' -ForegroundColor Cyan
dotnet nuget locals http-cache,temp --clear | Out-Host

Write-Host '[4/7] Restoring the complete solution...' -ForegroundColor Cyan
dotnet restore $solution --force --no-cache | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Solution restore failed.' }

Write-Host '[5/7] Verifying the WebView2 package...' -ForegroundColor Cyan
$package = dotnet list $desktopProject package --include-transitive | Out-String
if ($package -notmatch 'Microsoft\.Web\.WebView2') {
    throw 'Microsoft.Web.WebView2 was not restored for the Desktop project.'
}

Write-Host '[6/7] Building the complete solution...' -ForegroundColor Cyan
dotnet build $solution -c $Configuration --no-restore | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }

Write-Host '[7/7] Checking the Edge WebView2 Runtime...' -ForegroundColor Cyan
$runtimeKeys = @(
 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F1E7E4E5-...}',
 'HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients'
)
$runtimeFound = $false
foreach ($base in @('HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients','HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients','HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients')) {
    if (Test-Path $base) {
        $runtimeFound = (Get-ChildItem $base -ErrorAction SilentlyContinue | ForEach-Object { Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue } | Where-Object { $_.name -like '*WebView2*' }).Count -gt 0
        if ($runtimeFound) { break }
    }
}
if (-not $runtimeFound) {
    Write-Warning 'The project compiled, but the Microsoft Edge WebView2 Runtime was not detected. Install the Evergreen Runtime before using the Visual Editor.'
}

Write-Host 'WebView2 restore and build validation completed.' -ForegroundColor Green
