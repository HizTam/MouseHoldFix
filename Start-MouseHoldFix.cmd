@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-App.ps1" %*
exit /b %ERRORLEVEL%
