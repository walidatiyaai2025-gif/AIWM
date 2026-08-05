@echo off
setlocal EnableExtensions
title AI WordPress Manager - Full Bootstrap

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

rem Default installation folder. Change this value if needed.
set "INSTALL_ROOT=C:\Apps"

echo.
echo ============================================================
echo  AI WordPress Manager - Prerequisites, Clone, Build and Run
echo ============================================================
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" -InstallRoot "%INSTALL_ROOT%"
set "EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%EXIT_CODE%"=="0" (
    echo [ERROR] Bootstrap failed with exit code %EXIT_CODE%.
) else (
    echo [SUCCESS] Bootstrap completed successfully.
)

pause
exit /b %EXIT_CODE%
