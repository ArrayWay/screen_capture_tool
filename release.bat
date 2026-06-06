@echo off
setlocal
set SCRIPT_DIR=%~dp0
set SHOULD_PAUSE=
if "%~1"=="" set SHOULD_PAUSE=1

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%scripts\release.ps1" %*
set EXIT_CODE=%ERRORLEVEL%

if not "%EXIT_CODE%"=="0" (
    echo.
    echo Release script failed with exit code %EXIT_CODE%.
    set SHOULD_PAUSE=1
)

if defined SHOULD_PAUSE (
    echo.
    pause
)

endlocal & exit /b %EXIT_CODE%
