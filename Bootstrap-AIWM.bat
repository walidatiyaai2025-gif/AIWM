@echo off
setlocal EnableExtensions
title AI WordPress Manager - Fresh Install, Build and Run

set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%Bootstrap-AIWM.ps1"

if not exist "%PS_SCRIPT%" (
    echo.
    echo [ERROR] Bootstrap-AIWM.ps1 was not found:
    echo %PS_SCRIPT%
    echo.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  AI WordPress Manager - Fresh Install, Build and Run
echo ============================================================
echo.

powershell.exe -NoLogo -NoProfile -NoExit -ExecutionPolicy Bypass -File "%PS_SCRIPT%"
set "EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%EXIT_CODE%"=="0" (
    echo [ERROR] Bootstrap failed with exit code %EXIT_CODE%.
) else (
    echo [SUCCESS] Bootstrap completed successfully.
)

pause
exit /b %EXIT_CODE%
