@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo ==========================================
echo 1. Stopping Lertaro background service and frontend App...
echo ==========================================
echo Stopping frontend App...
taskkill /f /im Lertaro.App.exe >nul 2>&1
powershell -Command "Start-Process taskkill -ArgumentList '/f /im Lertaro.App.exe' -Verb RunAs -WindowStyle Hidden -Wait"


echo Requesting Administrator privileges to stop LertaroService...
powershell -Command "Start-Process net -ArgumentList 'stop LertaroService' -Verb RunAs -WindowStyle Hidden -Wait"

echo Requesting Administrator privileges to kill hook subprocess (Lertaro.Service.exe)...
powershell -Command "Start-Process taskkill -ArgumentList '/f /im Lertaro.Service.exe' -Verb RunAs -WindowStyle Hidden -Wait"

echo Stopping any running lff.exe (CLI)...
taskkill /f /im lff.exe >nul 2>&1
powershell -Command "Start-Process taskkill -ArgumentList '/f /im lff.exe' -Verb RunAs -WindowStyle Hidden -Wait"

:: Wait a moment for file handles to be completely released
ping 127.0.0.1 -n 3 >nul

echo Cleaning up debug directory...
if exist "%~dp0debug" (
    rmdir /s /q "%~dp0debug" >nul 2>&1
    if exist "%~dp0debug" (
        ping 127.0.0.1 -n 2 >nul
        rmdir /s /q "%~dp0debug"
    )
)

echo ==========================================
echo 2. Building projects (dotnet build directly to debug directory)...
echo ==========================================
dotnet build Lertaro.slnx --no-incremental /p:OutputPath="%~dp0debug/" >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Main program compilation failed, please check the build output!
    pause
    exit /b
)

dotnet build Lertaro.Plugins.slnx --no-incremental /p:OutputPath="%~dp0debug/Plugins/" >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Plugins compilation failed, please check the build output!
    pause
    exit /b
)

echo.
echo ==========================================
echo 3. Launching WPF frontend application with standard user privileges...
echo ==========================================
powershell -Command "Start-Process -FilePath '%~dp0debug\Lertaro.App.exe' -WorkingDirectory '%~dp0debug'"

echo Build and run script completed successfully.
