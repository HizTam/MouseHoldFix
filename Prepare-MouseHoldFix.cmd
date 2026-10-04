@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem Keep paths in an environment variable instead of inserting them into PowerShell source.
set "MOUSEHOLDFIX_PREPARATION_SCRIPT=%~dp0Unblock-MouseHoldFix.ps1"
if not exist "%MOUSEHOLDFIX_PREPARATION_SCRIPT%" (
    echo Unblock-MouseHoldFix.ps1 was not found. Extract the entire ZIP before preparing the app.
    pause
    exit /b 1
)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; Unblock-File -LiteralPath $env:MOUSEHOLDFIX_PREPARATION_SCRIPT; & $env:MOUSEHOLDFIX_PREPARATION_SCRIPT"
set "MOUSEHOLDFIX_PREPARATION_RESULT=%ERRORLEVEL%"
if not "%MOUSEHOLDFIX_PREPARATION_RESULT%"=="0" echo Preparation failed. Read the error above before starting MouseHoldFix.exe.
echo.
pause
exit /b %MOUSEHOLDFIX_PREPARATION_RESULT%
