@echo off
setlocal
set SCRIPT_DIR=%~dp0
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%scripts\release.ps1" %*
set EXIT_CODE=%ERRORLEVEL%
if not "%EXIT_CODE%"=="0" (
    echo.
    echo Release script failed with exit code %EXIT_CODE%.
)
endlocal & exit /b %EXIT_CODE%
