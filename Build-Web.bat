@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title AI WordPress Manager - Build Web

set "PROJECT=%CD%\src\AIWordPressManager.Web\AIWordPressManager.Web.csproj"
set "LOG_FILE=%CD%\build-web.log"

if not exist "%PROJECT%" (
    echo [INFO] Web project was not found in this repository.
    echo Expected path:
    echo %PROJECT%
    echo.
    echo This tool will work automatically when the Web project is present.
    pause
    exit /b 0
)

call "%CD%\Kill-All-Processes.bat" /quiet
if errorlevel 1 goto :failed

call "%CD%\Clean-All.bat" /quiet
if errorlevel 1 goto :failed

>"%LOG_FILE%" echo Web build started: %DATE% %TIME%
dotnet restore "%PROJECT%" --force >>"%LOG_FILE%" 2>&1
if errorlevel 1 goto :failed

dotnet build "%PROJECT%" -c Debug --no-restore >>"%LOG_FILE%" 2>&1
if errorlevel 1 goto :failed

echo [OK] Web build succeeded.
echo Starting Web application...
dotnet run --no-build --project "%PROJECT%"
exit /b %ERRORLEVEL%

:failed
echo [ERROR] Web build failed. Review %LOG_FILE%
start "" notepad "%LOG_FILE%"
pause
exit /b 1
