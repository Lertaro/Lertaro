@echo off
setlocal
chcp 65001 >nul

if /I not "%~1"=="--elevated" (
    echo Removing the per-user lertaro:// URI registration...
    reg.exe delete "HKCU\Software\Classes\lertaro" /f >nul 2>&1
    if errorlevel 1 (
        echo No lertaro:// URI registration was found.
    ) else (
        echo The lertaro:// URI registration was removed.
    )

    echo Removing the per-user startup entry...
    reg.exe delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "Lertaro" /f >nul 2>&1
    if errorlevel 1 (
        echo No Lertaro startup entry was found.
    ) else (
        echo The Lertaro startup entry was removed.
    )
)

:: Service removal changes the machine-wide service registration, so run the script elevated.
net session >nul 2>&1
if errorlevel 1 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList '--elevated' -Verb RunAs -WorkingDirectory '%~dp0'"
    exit /b 0
)

set "SERVICE_NAME=LertaroService"

echo Checking for %SERVICE_NAME%...
sc.exe query "%SERVICE_NAME%" >nul 2>&1
if errorlevel 1 (
    echo %SERVICE_NAME% is not installed.
    goto :done
)

echo Stopping %SERVICE_NAME%...
sc.exe stop "%SERVICE_NAME%" >nul 2>&1

set /a WAIT_SECONDS=0
:wait_for_stop
sc.exe query "%SERVICE_NAME%" 2>nul | findstr /I "STOPPED" >nul
if not errorlevel 1 goto :delete_service
if %WAIT_SECONDS% geq 30 goto :delete_service
set /a WAIT_SECONDS+=1
timeout /t 1 /nobreak >nul
goto :wait_for_stop

:delete_service
echo Removing %SERVICE_NAME%...
sc.exe delete "%SERVICE_NAME%" >nul 2>&1
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
