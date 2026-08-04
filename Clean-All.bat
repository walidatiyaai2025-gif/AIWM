@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

set "QUIET=0"
if /I "%~1"=="/quiet" set "QUIET=1"

if "%QUIET%"=="0" (
    title AI WordPress Manager - Clean All
    echo ============================================================
    echo   Cleaning all bin and obj folders
    echo ============================================================
)

call "%CD%\Kill-All-Processes.bat" /quiet
if errorlevel 1 exit /b 1

dotnet build-server shutdown >nul 2>&1

set "FAILED=0"
for /d /r %%D in (bin,obj) do (
    if exist "%%D" (
        if "%QUIET%"=="0" echo Removing %%D
        rd /s /q "%%D" >nul 2>&1
        if exist "%%D" set "FAILED=1"
    )
)

if "%FAILED%"=="1" (
    if "%QUIET%"=="0" echo [ERROR] One or more folders could not be removed.
    exit /b 1
)

if "%QUIET%"=="0" (
    echo [OK] Clean completed.
    pause
)
exit /b 0
