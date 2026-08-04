$ErrorActionPreference = 'Stop'

Write-Host 'Checking .NET SDK...'
dotnet --version

Write-Host 'Restoring packages...'
dotnet restore .\AIWordPressManager.sln

Write-Host 'Building solution...'
dotnet build .\AIWordPressManager.sln -c Debug --no-restore

Write-Host 'Running tests...'
dotnet test .\AIWordPressManager.sln -c Debug --no-build

Write-Host 'Phase 1 / Part 1 verification completed.' -ForegroundColor Green
