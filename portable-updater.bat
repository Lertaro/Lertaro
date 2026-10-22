@echo off
:: Manual update of a portable copy, for the case the in-app updater cannot serve: the folder no longer
:: starts, the service was never installed, or the user wants to copy a downloaded build in by hand.
::
::   portable-updater.bat "<folder with the new version>" ["<installation folder>"]
::
:: The installation folder defaults to the folder this script is in. The script has to run elevated:
:: installing the service locks the application folder against Users, so the copy needs an administrator.
setlocal EnableDelayedExpansion
set "SYS=%SystemRoot%\System32"
if not exist "%SYS%\fltmc.exe" (
    echo [ERROR] Could not find the system tools in "%SYS%".
    pause
    exit /b 1
)
"%SYS%\chcp.com" 65001 >nul

set "RELAY="
if /I "%~1"=="--elevated" (
    set "RELAY=%~2"
    < "%RELAY%" (set /p SOURCE= & set /p DESTINATION=)
    goto :update
)

if "%~1"=="" goto :usage
set "SOURCE=%~1"
set "DESTINATION=%~2"
if not defined DESTINATION set "DESTINATION=%~dp0"

:: Through a file, not through Start-Process arguments: the paths are chosen by the user and may contain
:: spaces, and quoting an argument list that is re-parsed by cmd is where those paths get lost.
:: Delayed expansion keeps a path containing & | < > ^ from being read as command syntax.
set "RELAY=%TEMP%\lertaro-update-%~RANDOM%%~RANDOM%.args"
> "%RELAY%" echo(!SOURCE!
>> "%RELAY%" echo(!DESTINATION!

:: fltmc rather than `net session`: the latter fails when the LanmanServer service is off, which read as
:: "not admin" inside an already elevated process.
"%SYS%\fltmc.exe" >nul 2>&1
if not errorlevel 1 goto :update

echo Administrator permission is required to replace the files in the installation folder.
"%SYS%\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '--elevated','%RELAY%' -Verb RunAs"
exit /b 0

:update
"%SYS%\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0portable-updater.ps1" -Source "!SOURCE!" -Destination "!DESTINATION!" -Manual
set "CODE=%ERRORLEVEL%"
if defined RELAY del "%RELAY%" >nul 2>&1
echo.
if %CODE% neq 0 (
    echo The update did not complete. The reason is above, and in update-result.json in the installation folder.
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
