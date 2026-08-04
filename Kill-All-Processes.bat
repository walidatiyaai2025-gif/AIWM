@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "QUIET=0"
if /I "%~1"=="/quiet" set "QUIET=1"

if "%QUIET%"=="0" (
    title AI WordPress Manager - Stop Processes
    echo ============================================================
    echo   Stopping AI WordPress Manager processes
    echo ============================================================
)

for %%P in (
    AIWordPressManager.Desktop.exe
    AIWordPressManager.Web.exe
    iisexpress.exe
) do (
    taskkill /F /IM %%P >nul 2>&1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$names = @('AIWordPressManager.Desktop','AIWordPressManager.Web');" ^
  "Get-Process -ErrorAction SilentlyContinue | Where-Object { $names -contains $_.ProcessName } | Stop-Process -Force -ErrorAction SilentlyContinue;" ^
  "$targets = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'AIWordPressManager\.(Desktop|Web)' };" ^
  "foreach ($p in $targets) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue };" ^
  "Start-Sleep -Milliseconds 700"

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$remaining = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { ($_.Name -match '^AIWordPressManager\.(Desktop|Web)\.exe$') -or ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'AIWordPressManager\.(Desktop|Web)') }; if ($remaining) { exit 1 }"

if errorlevel 1 (
    if "%QUIET%"=="0" echo [ERROR] Some AI WordPress Manager processes are still running.
    exit /b 1
)

if "%QUIET%"=="0" (
    echo [OK] Application processes stopped.
    pause
)
exit /b 0
