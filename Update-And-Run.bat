@echo off
setlocal
title AI WordPress Manager - Update and Run

set "SCRIPT_DIR=%~dp0"

echo.
echo AI WordPress Manager - Update and Run
echo Project path: %SCRIPT_DIR%
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Update-And-Run.ps1" -ProjectPath "%SCRIPT_DIR%"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo Update or build failed with exit code %EXIT_CODE%.
    if exist "%SCRIPT_DIR%update-and-run.log" start "" notepad.exe "%SCRIPT_DIR%update-and-run.log"
    pause
    exit /b %EXIT_CODE%
)

exit /b 0
