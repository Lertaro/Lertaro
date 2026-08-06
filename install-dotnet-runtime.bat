@echo off
setlocal

:: Silently installs the .NET 10 Desktop Runtime if it isn't already present -- same check/URL/install
:: args as Installer\installer.iss's IsDotNet10Installed/PrepareToInstall, for the portable ZIP build
:: (which, unlike the installer, has no bootstrap of its own).

:: Which runtime this needs follows the architecture of the app sitting next to this script, read out of
:: its PE header, rather than the machine's own. Those differ in the case that matters: the x64 package
:: on an arm64 machine needs the x64 runtime, because the app in it is x64 and runs emulated. Asking
:: Windows what IT is would fetch arm64 there and leave the app still unable to start. One copy of this
:: script ships in both packages, so it has to work this out at run time rather than being baked in.
set "ARCH_SUFFIX=x64"
if exist "%~dp0Lertaro.App.exe" (
    for /f "usebackq tokens=*" %%a in (`powershell -NoProfile -Command "try { $b=[IO.File]::ReadAllBytes('%~dp0Lertaro.App.exe'); $p=[BitConverter]::ToUInt32($b,0x3C); if ([BitConverter]::ToUInt16($b,$p+4) -eq 0xAA64) { 'arm64' } else { 'x64' } } catch { 'x64' }"`) do set "ARCH_SUFFIX=%%a"
)

:: And where an already-installed one would live follows from that too. On an arm64 machine the arm64
:: runtime installs under dotnet\ and the x64 one under dotnet\x64\, so an x64 package there has to look
:: in the subdirectory: checking dotnet\ would find the ARM64 runtime, decide everything was in place,
:: and skip an install the x64 app cannot start without. Getting this wrong in the other direction only
:: costs a redundant download, since the runtime installer exits quickly when it is already present.
set "RUNTIME_ROOT=%ProgramFiles%\dotnet"
if /i "%ARCH_SUFFIX%"=="x64" if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "RUNTIME_ROOT=%ProgramFiles%\dotnet\x64"
if /i "%ARCH_SUFFIX%"=="x64" if /i "%PROCESSOR_ARCHITEW6432%"=="ARM64" set "RUNTIME_ROOT=%ProgramFiles%\dotnet\x64"

set "RUNTIME_DIR=%RUNTIME_ROOT%\shared\Microsoft.WindowsDesktop.App"
set "FOUND=false"
if exist "%RUNTIME_DIR%" (
    for /d %%V in ("%RUNTIME_DIR%\10.*") do set "FOUND=true"
)
if "%FOUND%"=="true" exit /b 0

echo Installing .NET Desktop Runtime (%ARCH_SUFFIX%), please wait...

set "INSTALLER=%TEMP%\windowsdesktop-runtime-10-win-%ARCH_SUFFIX%.exe"
curl.exe -L --fail -o "%INSTALLER%" "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-%ARCH_SUFFIX%.exe"
if errorlevel 1 (
    echo Download failed.
    pause
    exit /b 1
)

"%INSTALLER%" /install /quiet /norestart
del /q "%INSTALLER%" >nul 2>&1
echo Done.
pause
exit /b 0
