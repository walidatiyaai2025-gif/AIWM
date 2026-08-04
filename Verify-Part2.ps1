$ErrorActionPreference = "Stop"
dotnet --version
dotnet restore .\AIWordPressManager.sln
dotnet build .\AIWordPressManager.sln -c Debug
dotnet test .\AIWordPressManager.sln -c Debug --no-build
Write-Host "Build and tests completed. Run the Desktop project to verify migration and database creation." -ForegroundColor Green
