@echo off
:: Manual update of a portable copy, for the case the in-app updater cannot serve: the folder no longer
:: starts, the service was never installed, or the user wants to copy a downloaded build in by hand.
::
::   portable-updater.bat "<folder with the new version>" ["<installation folder>"]
::
:: The installation folder defaults to the folder this script is in. PowerShell requests elevation
:: when needed: installing the service locks the application folder against Users.
setlocal DisableDelayedExpansion
set "SYS=%SystemRoot%\System32"
if not exist "%SYS%\WindowsPowerShell\v1.0\powershell.exe" (
    echo [ERROR] Could not find the system tools in "%SYS%".
    pause
    exit /b 1
)
"%SYS%\chcp.com" 65001 >nul

if "%~1"=="" goto :usage
:: Environment values preserve spaces, apostrophes and exclamation marks without putting paths in
:: PowerShell code or a user-writable argument relay file. The elevated process receives quoted arguments.
set "LERTARO_MANUAL_UPDATE_SOURCE=%~1"
set "LERTARO_MANUAL_UPDATE_DESTINATION=%~2"
if not defined LERTARO_MANUAL_UPDATE_DESTINATION set "LERTARO_MANUAL_UPDATE_DESTINATION=%~dp0"
"%SYS%\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0portable-updater.ps1" -Manual
set "CODE=%ERRORLEVEL%"
echo.
if %CODE% neq 0 (
    echo The update did not complete. Check update-result.json in the installation folder for the latest update result.
) else (
    echo The update completed.
)
pause
exit /b %CODE%

:usage
echo Usage: portable-updater.bat ^<folder with the new version^> [^<installation folder^>]
echo The installation folder defaults to the folder this script is in.
echo Close Lertaro before running this.
pause
exit /b 1
