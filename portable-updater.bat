@echo off
setlocal
if "%~3"=="" exit /b 1
"%~3\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -File "%~dp0portable-updater.ps1" -Source "%~1" -Destination "%~2"
exit /b %errorlevel%
