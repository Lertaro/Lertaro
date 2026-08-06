@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul

:: 1. Check for Admin privileges and self-elevate
net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process -FilePath '%~f0' -ArgumentList '\"%~1\" \"%~2\"' -Verb RunAs"
    exit /b
)

:: %1: The source directory containing the new version files (unzipped temporary directory)
:: %2: The target installation directory of the current Lertaro instance
set "SRC_DIR=%~1"
set "DST_DIR=%~2"

if "%SRC_DIR%"=="" exit /b 1
if "%DST_DIR%"=="" exit /b 1

:KillApp
tasklist /FI "IMAGENAME eq Lertaro.App.exe" 2>NUL | find /I /N "Lertaro.App.exe" >NUL
if "%errorlevel%"=="0" (
    taskkill /F /IM Lertaro.App.exe >nul 2>&1
    timeout /t 1 /nobreak >nul
    goto KillApp
)

sc stop LertaroService >nul 2>&1
timeout /t 1 /nobreak >nul

:KillService
tasklist /FI "IMAGENAME eq Lertaro.Service.exe" 2>NUL | find /I /N "Lertaro.Service.exe" >NUL
if "%errorlevel%"=="0" (
    taskkill /F /IM Lertaro.Service.exe >nul 2>&1
    timeout /t 1 /nobreak >nul
    goto KillService
)

:: lff.exe (the CLI companion) sits in the same DST_DIR as everything else below -- if a copy of it
:: is open in some terminal window right now, xcopy can't overwrite its locked file.
:KillLff
tasklist /FI "IMAGENAME eq lff.exe" 2>NUL | find /I /N "lff.exe" >NUL
if "%errorlevel%"=="0" (
    taskkill /F /IM lff.exe >nul 2>&1
    timeout /t 1 /nobreak >nul
    goto KillLff
)

:: Copy new files to destination directory, overwriting existing files
xcopy "%SRC_DIR%\*" "%DST_DIR%\" /E /Y /Q /R

:: Re-start the background service
sc start LertaroService >nul 2>&1

:: Run Lertaro.App.exe as standard user via explorer.exe to avoid running App as administrator
start "" explorer.exe "%DST_DIR%\Lertaro.App.exe"

exit /b 0
