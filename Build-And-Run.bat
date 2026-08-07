@echo off
setlocal EnableExtensions EnableDelayedExpansion

cd /d "%~dp0"
title AI WordPress Manager - Update, Build and Run
color 0A

set "REPOSITORY_URL=https://github.com/walidatiyaai2025-gif/AIWM.git"
set "TARGET_BRANCH=main"
set "LOG_FILE=%CD%\build-and-run.log"
set "SOLUTION=%CD%\AIWordPressManager.sln"
set "DESKTOP_PROJECT=%CD%\src\AIWordPressManager.Desktop\AIWordPressManager.Desktop.csproj"

> "%LOG_FILE%" echo AI WordPress Manager Build and Run Log
>>"%LOG_FILE%" echo Started: %DATE% %TIME%
>>"%LOG_FILE%" echo Project: %CD%
>>"%LOG_FILE%" echo Repository: %REPOSITORY_URL%
>>"%LOG_FILE%" echo Target branch: %TARGET_BRANCH%
>>"%LOG_FILE%" echo.

echo ============================================================
echo   AI WordPress Manager - Update, Clean, Build and Run
echo   Branch: %TARGET_BRANCH%
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

if not exist ".git" (
    echo [ERROR] This folder is not a Git repository: %CD%
    >>"%LOG_FILE%" echo [ERROR] Missing .git folder.
    goto :failed
)

if not exist "%SOLUTION%" (
    echo [ERROR] Solution file not found: %SOLUTION%
    >>"%LOG_FILE%" echo [ERROR] Solution file not found.
    goto :failed
)

if not exist "%DESKTOP_PROJECT%" (
    echo [ERROR] Desktop project not found: %DESKTOP_PROJECT%
    >>"%LOG_FILE%" echo [ERROR] Desktop project not found.
    goto :failed
)

echo [1/10] Stopping running AI WordPress Manager processes...
call "%CD%\Kill-All-Processes.bat" /quiet >>"%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo [ERROR] Could not stop one or more application processes.
    >>"%LOG_FILE%" echo [ERROR] Process shutdown failed.
    goto :failed
)

echo [2/10] Checking local Git state...
git status --short >>"%LOG_FILE%" 2>&1
if errorlevel 1 goto :git_failed

for /f "delims=" %%A in ('git status --porcelain') do set "HAS_LOCAL_CHANGES=1"
if defined HAS_LOCAL_CHANGES (
    echo [INFO] Local changes detected. Saving them to Git stash...
    >>"%LOG_FILE%" echo [INFO] Local changes detected. Creating stash backup.
    git stash push --include-untracked -m "Build-And-Run backup before switching to %TARGET_BRANCH%" >>"%LOG_FILE%" 2>&1
    if errorlevel 1 goto :git_failed
    echo [INFO] Local changes were preserved in Git stash.
)

echo [3/10] Fetching latest repository data...
git fetch origin --prune >>"%LOG_FILE%" 2>&1
if errorlevel 1 goto :git_failed

echo [4/10] Switching to branch %TARGET_BRANCH%...
git checkout "%TARGET_BRANCH%" >>"%LOG_FILE%" 2>&1
if errorlevel 1 (
    git checkout -B "%TARGET_BRANCH%" "origin/%TARGET_BRANCH%" >>"%LOG_FILE%" 2>&1
    if errorlevel 1 goto :git_failed
)

echo [5/10] Updating local branch from origin/%TARGET_BRANCH%...
git reset --hard "origin/%TARGET_BRANCH%" >>"%LOG_FILE%" 2>&1
if errorlevel 1 goto :git_failed

for /f "delims=" %%A in ('git rev-parse HEAD') do set "SOURCE_COMMIT=%%A"
if not defined SOURCE_COMMIT set "SOURCE_COMMIT=unknown"
>>"%LOG_FILE%" echo Source commit: %SOURCE_COMMIT%

echo [6/10] Stopping .NET build servers...
dotnet build-server shutdown >>"%LOG_FILE%" 2>&1

echo [7/10] Removing bin and obj folders...
call "%CD%\Clean-All.bat" /quiet >>"%LOG_FILE%" 2>&1
if errorlevel 1 goto :clean_failed

echo [8/10] Restoring NuGet packages...
dotnet restore "%SOLUTION%" --force --disable-parallel --nologo >>"%LOG_FILE%" 2>&1
if errorlevel 1 goto :restore_failed

echo [9/10] Building the Debug solution...
dotnet build "%SOLUTION%" -c Debug --no-restore --nologo --maxcpucount:1 /p:SourceBranchName="%TARGET_BRANCH%" /p:SourceCommitSha="%SOURCE_COMMIT%" >>"%LOG_FILE%" 2>&1
if errorlevel 1 goto :build_failed

echo [10/10] Starting AI WordPress Manager Desktop...
echo.
>>"%LOG_FILE%" echo Build succeeded: %DATE% %TIME%
>>"%LOG_FILE%" echo Embedded source branch: %TARGET_BRANCH%
>>"%LOG_FILE%" echo Embedded source commit: %SOURCE_COMMIT%
>>"%LOG_FILE%" echo Commit:
git log -1 --oneline >>"%LOG_FILE%" 2>&1

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
echo [ERROR] Git update failed. Review the log for details.
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
echo   Branch: %TARGET_BRANCH%
echo   Commit: %SOURCE_COMMIT%
echo ============================================================
if defined HAS_LOCAL_CHANGES (
    echo [INFO] Your previous local changes are saved in Git stash.
    echo [INFO] Run: git stash list
)
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
