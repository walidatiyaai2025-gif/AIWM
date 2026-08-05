@echo off
setlocal EnableExtensions
title AI WordPress Manager - Sync Task Tracker

set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%Sync-Task-Tracker.ps1"

if not exist "%PS_SCRIPT%" (
    echo.
    echo [ERROR] Sync-Task-Tracker.ps1 was not found:
    echo %PS_SCRIPT%
    echo.
    pause
    exit /b 1
)

pushd "%SCRIPT_DIR%"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%"
set "EXIT_CODE=%ERRORLEVEL%"
popd

echo.
if not "%EXIT_CODE%"=="0" (
    echo [ERROR] Task tracker synchronization failed with exit code %EXIT_CODE%.
) else (
    echo [SUCCESS] Task tracker synchronization completed.
)

pause
exit /b %EXIT_CODE%
