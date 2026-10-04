@echo off
setlocal

:: SYS: System32, taken from SystemRoot rather than from PATH or PATHEXT. Every tool below is called by full
::     path from here: the elevated copy of this script runs in an environment built from the user's own
::     settings, where PATH and PATHEXT are the user's to change and a matching name in them would run at
::     administrator integrity.
set "SYS=%SystemRoot%\System32"
if not exist "%SYS%\reg.exe" (
    echo [ERROR] Could not find the system tools in "%SYS%".
    pause
    exit /b 1
)

"%SYS%\chcp.com" 65001 >nul

if /I not "%~1"=="--elevated" (
    echo Removing the per-user lertaro:// URI registration...
    "%SYS%\reg.exe" delete "HKCU\Software\Classes\lertaro" /f >nul 2>&1
    if errorlevel 1 (
        echo No lertaro:// URI registration was found.
    ) else (
        echo The lertaro:// URI registration was removed.
    )

    echo Removing the per-user startup entry...
    "%SYS%\reg.exe" delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "Lertaro" /f >nul 2>&1
    if errorlevel 1 (
        echo No Lertaro startup entry was found.
    ) else (
        echo The Lertaro startup entry was removed.
    )
)

:: Service removal changes the machine-wide service registration, so run the script elevated.
:: fltmc and not the usual `net session`: the latter only succeeds when the LanmanServer service is
:: running, so on a machine where that has been turned off it reported "not admin" inside an already
:: elevated process -- which sent this branch around again, asking for UAC over and over with no
:: elevation ever reached. portable-updater.bat stopped using it for the same reason.
"%SYS%\fltmc.exe" >nul 2>&1
if errorlevel 1 (
    "%SYS%\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList '--elevated' -Verb RunAs -WorkingDirectory '%~dp0'"
    exit /b 0
)

set "SERVICE_NAME=LertaroService"

echo Checking for %SERVICE_NAME%...
"%SYS%\sc.exe" query "%SERVICE_NAME%" >nul 2>&1
if errorlevel 1 (
    echo %SERVICE_NAME% is not installed.
    goto :done
)

echo Stopping %SERVICE_NAME%...
"%SYS%\sc.exe" stop "%SERVICE_NAME%" >nul 2>&1

set /a WAIT_SECONDS=0
:wait_for_stop
"%SYS%\sc.exe" query "%SERVICE_NAME%" 2>nul | "%SYS%\findstr.exe" /I "STOPPED" >nul
if not errorlevel 1 goto :delete_service
if %WAIT_SECONDS% geq 30 goto :delete_service
set /a WAIT_SECONDS+=1
"%SYS%\timeout.exe" /t 1 /nobreak >nul
goto :wait_for_stop

:delete_service
echo Removing %SERVICE_NAME%...
"%SYS%\sc.exe" delete "%SERVICE_NAME%" >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Failed to remove %SERVICE_NAME%.
    echo The service may still be stopping. Try this script again after a few seconds.
    set "EXIT_CODE=1"
) else (
    echo %SERVICE_NAME% was removed successfully.
    set "EXIT_CODE=0"
)

:done
if not defined EXIT_CODE set "EXIT_CODE=0"
echo.
pause
exit /b %EXIT_CODE%
