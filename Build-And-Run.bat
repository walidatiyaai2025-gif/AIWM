@echo off
setlocal EnableExtensions EnableDelayedExpansion

cd /d "%~dp0"
title AI WordPress Manager - Update, Build and Run
color 0A

set "LOG_FILE=%CD%\build-and-run.log"
set "SOLUTION=%CD%\AIWordPressManager.sln"
set "DESKTOP_PROJECT=%CD%\src\AIWordPressManager.Desktop\AIWordPressManager.Desktop.csproj"

> "%LOG_FILE%" echo AI WordPress Manager Build and Run Log
>>"%LOG_FILE%" echo Started: %DATE% %TIME%
>>"%LOG_FILE%" echo Project: %CD%
>>"%LOG_FILE%" echo.

echo ============================================================
echo   AI WordPress Manager - Update, Clean, Build and Run
echo ============================================================
echo.

where git >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Git was not found in PATH.
    >>"%LOG_FILE%" echo [ERROR] Git was not found in PATH.
    goto :failed
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK was not found in PATH.
    >>"%LOG_FILE%" echo [ERROR] .NET SDK was not found in PATH.
    goto :failed
)

if not exist "%SOLUTION%" (
    echo [ERROR] Solution file not found: %SOLUTION%
    >>"%LOG_FILE%" echo [ERROR] Solution file not found: %SOLUTION%
    goto :failed
)

if not exist "%DESKTOP_PROJECT%" (
    echo [ERROR] Desktop project not found: %DESKTOP_PROJECT%
    >>"%LOG_FILE%" echo [ERROR] Desktop project not found: %DESKTOP_PROJECT%
    goto :failed
)

echo [1/8] Stopping running AI WordPress Manager processes...
call "%CD%\Kill-All-Processes.bat" /quiet >>"%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo [ERROR] Could not stop one or more application processes.
    >>"%LOG_FILE%" echo [ERROR] Process shutdown failed.
    goto :failed
)

echo [2/8] Checking local Git state...
git status --short >>"%LOG_FILE%" 2>&1
if errorlevel 1 goto :git_failed

echo [3/8] Pulling the latest main branch...
git pull origin main >>"%LOG_FILE%" 2>&1
if errorlevel 1 goto :git_failed

echo [4/8] Stopping .NET build servers...
dotnet build-server shutdown >>"%LOG_FILE%" 2>&1

echo [5/8] Removing bin and obj folders...
call "%CD%\Clean-All.bat" /quiet >>"%LOG_FILE%" 2>&1
if errorlevel 1 goto :clean_failed

echo [6/8] Restoring NuGet packages...
dotnet restore "%SOLUTION%" --force >>"%LOG_FILE%" 2>&1
if errorlevel 1 goto :restore_failed

echo [7/8] Building the Debug solution...
dotnet build "%SOLUTION%" -c Debug --no-restore >>"%LOG_FILE%" 2>&1
if errorlevel 1 goto :build_failed

echo [8/8] Starting AI WordPress Manager Desktop...
echo.
>>"%LOG_FILE%" echo Build succeeded: %DATE% %TIME%

dotnet run --no-build --project "%DESKTOP_PROJECT%"
set "RUN_EXIT=%ERRORLEVEL%"

if not "%RUN_EXIT%"=="0" (
    echo.
    echo [ERROR] The application exited with code %RUN_EXIT%.
    >>"%LOG_FILE%" echo [ERROR] Application exit code: %RUN_EXIT%
    goto :failed
)

goto :success

:git_failed
echo [ERROR] Git update failed. Local changes or authentication may require attention.
>>"%LOG_FILE%" echo [ERROR] Git update failed.
goto :failed

:clean_failed
echo [ERROR] Cleaning bin and obj folders failed. A process may still be locking files.
>>"%LOG_FILE%" echo [ERROR] Clean failed.
goto :failed

:restore_failed
echo [ERROR] NuGet restore failed.
>>"%LOG_FILE%" echo [ERROR] NuGet restore failed.
goto :failed

:build_failed
echo [ERROR] Build failed. Opening the log file...
>>"%LOG_FILE%" echo [ERROR] Build failed.
start "" notepad "%LOG_FILE%"
goto :failed

:success
echo.
echo ============================================================
echo   Completed successfully.
echo ============================================================
>>"%LOG_FILE%" echo Completed successfully: %DATE% %TIME%
exit /b 0

:failed
echo.
echo ============================================================
echo   PROCESS FAILED
echo   Review: %LOG_FILE%
echo ============================================================
echo.
pause
exit /b 1
