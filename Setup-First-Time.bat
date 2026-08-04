@echo off
setlocal
title AI WordPress Manager - First Time Setup

set "SCRIPT_DIR=%~dp0"
set "INSTALL_PATH=%USERPROFILE%\AIWordPressManager"

if not "%~1"=="" set "INSTALL_PATH=%~1"

echo.
echo AI WordPress Manager - First Time Setup
echo Install path: %INSTALL_PATH%
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Setup-First-Time.ps1" -InstallPath "%INSTALL_PATH%"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo Setup failed with exit code %EXIT_CODE%.
    pause
    exit /b %EXIT_CODE%
)

echo.
echo Setup completed successfully.
pause
exit /b 0
