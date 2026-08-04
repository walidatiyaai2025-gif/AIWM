@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title AI WordPress Manager - Build Desktop

set "PROJECT=%CD%\src\AIWordPressManager.Desktop\AIWordPressManager.Desktop.csproj"
set "LOG_FILE=%CD%\build-desktop.log"

if not exist "%PROJECT%" (
    echo [ERROR] Desktop project not found: %PROJECT%
    pause
    exit /b 1
)

call "%CD%\Kill-All-Processes.bat" /quiet
if errorlevel 1 goto :failed

call "%CD%\Clean-All.bat" /quiet
if errorlevel 1 goto :failed

>"%LOG_FILE%" echo Desktop build started: %DATE% %TIME%
dotnet restore "%PROJECT%" --force >>"%LOG_FILE%" 2>&1
if errorlevel 1 goto :failed

dotnet build "%PROJECT%" -c Debug --no-restore >>"%LOG_FILE%" 2>&1
if errorlevel 1 goto :failed

echo [OK] Desktop build succeeded.
echo Starting Desktop application...
dotnet run --no-build --project "%PROJECT%"
exit /b %ERRORLEVEL%

:failed
echo [ERROR] Desktop build failed. Review %LOG_FILE%
start "" notepad "%LOG_FILE%"
pause
exit /b 1
