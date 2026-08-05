@echo off
setlocal EnableExtensions
title AI WordPress Manager - Full Bootstrap

set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%Bootstrap-AIWM.ps1"

if not exist "%PS_SCRIPT%" (
    echo.
    echo [ERROR] Bootstrap-AIWM.ps1 was not found beside this BAT file:
    echo %PS_SCRIPT%
    echo.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  AI WordPress Manager - Prerequisites, Clone, Build and Run
echo ============================================================
echo.
echo Working folder: %SCRIPT_DIR%
echo.

pushd "%SCRIPT_DIR%"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%"
set "EXIT_CODE=%ERRORLEVEL%"
popd

echo.
if not "%EXIT_CODE%"=="0" (
    echo [ERROR] Bootstrap failed with exit code %EXIT_CODE%.
) else (
    echo [SUCCESS] Bootstrap completed successfully.
)

pause
exit /b %EXIT_CODE%
