@echo off
setlocal enabledelayedexpansion

:: %3: System32, passed in by the service. Every tool below is called by full path from here: this script
::     runs elevated in an environment built from the user's own settings, where PATH, PATHEXT and
::     SystemRoot are the user's to change.
set "SYS=%~3"
if "%SYS%"=="" exit /b 1

"%SYS%\chcp.com" 65001 >nul

:: 1. Check for Admin privileges and self-elevate.
:: fltmc and not the usual `net session`: the latter only succeeds when the LanmanServer service is
:: running, so on a machine where that has been turned off it reported "not admin" inside an already
:: elevated process -- which sent this branch around again, asking for UAC over and over with no
:: elevation ever reached.
"%SYS%\fltmc.exe" >nul 2>&1
if %errorLevel% neq 0 (
    "%SYS%\WindowsPowerShell\v1.0\powershell.exe" -Command "Start-Process -FilePath '%~f0' -ArgumentList '\"%~1\" \"%~2\" \"%~3\"' -Verb RunAs"
    exit /b
)

:: %1: The source directory holding the new version files, already unpacked and signature-verified by the
::     background service. It sits under %2 on purpose: the temp directory it came from is writable by
::     unprivileged code, and an elevated copy step must not read its payload out of that.
:: %2: The target installation directory of the current Lertaro instance
set "SRC_DIR=%~1"
set "DST_DIR=%~2"

if "%SRC_DIR%"=="" exit /b 1
if "%DST_DIR%"=="" exit /b 1

:KillApp
"%SYS%\tasklist.exe" /FI "IMAGENAME eq Lertaro.App.exe" 2>NUL | "%SYS%\find.exe" /I /N "Lertaro.App.exe" >NUL
if "%errorlevel%"=="0" (
    "%SYS%\taskkill.exe" /F /IM Lertaro.App.exe >nul 2>&1
    "%SYS%\timeout.exe" /t 1 /nobreak >nul
    goto KillApp
)

"%SYS%\sc.exe" stop LertaroService >nul 2>&1
"%SYS%\timeout.exe" /t 1 /nobreak >nul

:KillService
"%SYS%\tasklist.exe" /FI "IMAGENAME eq Lertaro.Service.exe" 2>NUL | "%SYS%\find.exe" /I /N "Lertaro.Service.exe" >NUL
if "%errorlevel%"=="0" (
    "%SYS%\taskkill.exe" /F /IM Lertaro.Service.exe >nul 2>&1
    "%SYS%\timeout.exe" /t 1 /nobreak >nul
    goto KillService
)

:: lff.exe (the CLI companion) sits in the same DST_DIR as everything else below -- if a copy of it
:: is open in some terminal window right now, xcopy can't overwrite its locked file.
:KillLff
"%SYS%\tasklist.exe" /FI "IMAGENAME eq lff.exe" 2>NUL | "%SYS%\find.exe" /I /N "lff.exe" >NUL
if "%errorlevel%"=="0" (
    "%SYS%\taskkill.exe" /F /IM lff.exe >nul 2>&1
    "%SYS%\timeout.exe" /t 1 /nobreak >nul
    goto KillLff
)

:: Copy new files to destination directory, overwriting existing files
"%SYS%\xcopy.exe" "%SRC_DIR%\*" "%DST_DIR%\" /E /Y /Q /R

:: The service unpacks the verified payload into <install dir>\update-payload -- keep the name in step with
:: UpdateApplyRequestHandler.PayloadStagingFolderName -- and only the copy above may read from it. Its parent
:: goes too, whichever of the two layouts the release zip used.
rd /s /q "%DST_DIR%\update-payload" >nul 2>&1

:: Bring the service back: it was stopped to unlock its own files, and it is the one process that can start
:: the updated App at the session's own integrity level (see UpdateRelaunchMarker), which it does as it
:: comes up.
"%SYS%\sc.exe" start LertaroService >nul 2>&1
if %errorlevel% neq 0 (
    rem The service is deleted or disabled, so nothing else will start the App. This hand-off does not
    rem work from an elevated process -- UIPI drops the request to the session's non-elevated shell -- so
    rem it stays only as the fallback it now is, for the install that has no service to lean on.
    start "" "%SYS%\..\explorer.exe" "%DST_DIR%\Lertaro.App.exe"
)

exit /b 0
