@echo off
setlocal
chcp 65001 >nul

set "ROOT=%~dp0"
set "DIST=%ROOT%dist"

echo ==========================================
echo Lertaro Build and Package Script (make)
echo ==========================================

:: 1. Check for dotnet CLI
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [Error] dotnet CLI was not found. Please install the required .NET SDK.
    exit /b 1
)

:: 2. Clean and create the output directory (the per-architecture publish dirs are cleaned in :build_arch)
echo.
echo [1/3] Cleaning dist directory...
if exist "%DIST%" (
    rmdir /s /q "%DIST%" >nul 2>&1
    if exist "%DIST%" (
        timeout /t 1 >nul
        rmdir /s /q "%DIST%" >nul 2>&1
    )
)
mkdir "%DIST%"


:: 3. Find the 64-bit Inno Setup 7 compiler (hoisted ahead of both passes)
set "ISCC=C:\Program Files\Inno Setup 7\ISCC.exe"

if not exist "%ISCC%" (
    echo [Error] 64-bit Inno Setup 7 compiler ISCC.exe not found.
    echo Please install the 64-bit edition of Inno Setup 7.
    exit /b 1
)

"%ISCC%" /? 2>&1 | findstr /c:"Inno Setup 7" >nul
if errorlevel 1 (
    echo [Error] Inno Setup 7 is required to compile the installers.
    echo The selected ISCC.exe is not an Inno Setup 7 compiler.
    exit /b 1
)

:: 4. Extract application version from App.csproj (likewise shared by both passes)
echo.
echo Extracting application version...
for /f "usebackq tokens=*" %%v in (`powershell -NoProfile -Command "([xml](Get-Content '%ROOT%App\App.csproj')).Project.PropertyGroup.Version"`) do set "APP_VER=%%v"
for /f "usebackq tokens=*" %%v in (`powershell -NoProfile -Command "$a = '%APP_VER%.0.0.0' -split '\.'; $a[0..3] -join '.'"`) do set "APP_VER_4=%%v"
echo App Version: %APP_VER% (PE Version: %APP_VER_4%)


:: Two architectures, same steps. x64 publishes with no RID exactly as it always has, so its output is
:: unchanged; arm64 is a cross-publish from whatever machine is building. They are separate artifacts
:: rather than one combined installer because the BrowserData plugin carries a native library and only
:: one architecture's copy can sit in its (flat, deps.json-less) load directory -- see that project.
echo.
echo === Building x64 ===
set "ARCH=x64"
set "ARCHSUFFIX="
set "RIDARGS="
call :build_arch
if errorlevel 1 exit /b 1

echo.
echo === Building arm64 ===
set "ARCH=arm64"
:: The new repository is not visible to legacy installs, so use the conventional hyphenated suffix.
:: Kept in step with UpdateAssetSelector.SuffixFor and installer.iss.
set "ARCHSUFFIX=-arm64"
set "RIDARGS=-r win-arm64 --self-contained false"
call :build_arch
if errorlevel 1 exit /b 1

:: 5. Clean up temporary publish folder
echo.
echo Cleaning up temporary publish folder...
rmdir /s /q "%ROOT%publish" >nul 2>&1

echo.
echo ==========================================
echo Build and Package Completed Successfully!
echo Installer (x64):     %DIST%\Lertaro-Setup.exe
echo Portable ZIP (x64):  %DIST%\Lertaro-Portable.zip
echo Installer (arm64):   %DIST%\Lertaro-Setup-arm64.exe
echo Portable ZIP (arm64):%DIST%\Lertaro-Portable-arm64.zip
echo ==========================================
exit /b 0


:: ---------------------------------------------------------------------------------------------
:: Per-architecture build. Driven by ARCH / ARCHSUFFIX / RIDARGS set by the caller rather than by
:: parameters, because an empty argument (x64 passes no RID) is awkward to quote in batch.
:: ---------------------------------------------------------------------------------------------
:build_arch
set "OUT=%ROOT%publish\%ARCH%\Lertaro"
if exist "%OUT%" (
    rmdir /s /q "%OUT%" >nul 2>&1
    if exist "%OUT%" (
        timeout /t 1 >nul
        rmdir /s /q "%OUT%" >nul 2>&1
    )
)
mkdir "%OUT%"

:: Publish App/Service/Cli in Release mode
::
:: Solution-level `dotnet publish -o` prints NETSDK1194 ("specifying a solution-level output path...
:: may result in inconsistent builds") since it's not an officially supported publish mode -- every
:: project in Lertaro.slnx just publishes into the same -o in whatever order MSBuild picks, rather
:: than each publish being independently well-defined the way `dotnet publish SomeProject.csproj -o`
:: is. Used anyway (as Lertaro.Plugins.slnx already does below, without issue) since it's simpler to
:: maintain than a separate pushd/publish/popd block per exe project, and has been verified to produce
:: the same merged output (App+Service+Cli+Core+PluginSdk all landing in %OUT%) as the three separate
:: publishes it replaces.
echo.
echo [1/4] Publishing App/Service/Cli in Release mode...
dotnet publish "%ROOT%Lertaro.slnx" -c Release -o "%OUT%" %RIDARGS% -v quiet
set "SLN_EXIT=%errorlevel%"
if not "%SLN_EXIT%"=="0" (
    echo [Error] App/Service/Cli publish failed for %ARCH%.
    exit /b %SLN_EXIT%
)

:: Publish Plugins in Release mode
echo.
echo [2/4] Publishing Plugins in Release mode...
dotnet publish "%ROOT%Lertaro.Plugins.slnx" -c Release -o "%OUT%\Plugins" %RIDARGS% -v quiet
set "PLUGINS_EXIT=%errorlevel%"
if not "%PLUGINS_EXIT%"=="0" (
    echo [Error] Plugins publish failed for %ARCH%.
    exit /b %PLUGINS_EXIT%
)


:: Copy portable updater/cleanup files and clean PDB files
echo.
echo [3/4] Copying portable updater/cleanup files and cleaning PDB files...
copy "%ROOT%portable-updater.bat" "%OUT%\" >nul
if errorlevel 1 (
    echo [Warning] Failed to copy portable-updater.bat.
)
copy "%ROOT%install-dotnet-runtime.bat" "%OUT%\" >nul
if errorlevel 1 (
    echo [Warning] Failed to copy install-dotnet-runtime.bat.
)
copy "%ROOT%portable-cleanup.bat" "%OUT%\" >nul
if errorlevel 1 (
    echo [Warning] Failed to copy portable-cleanup.bat.
)
del /s /q "%OUT%\*.pdb" >nul 2>&1

:: Compile Inno Setup Installer
echo.
echo [4/4] Compiling Inno Setup Installer...
echo Using Inno Setup compiler: "%ISCC%"
"%ISCC%" /DAppVersion="%APP_VER%" /DAppVersion4="%APP_VER_4%" /DArch="%ARCH%" "%ROOT%Installer\installer.iss"
if errorlevel 1 (
    echo [Error] Inno Setup compilation failed for %ARCH%.
    exit /b 1
)

:: Create Portable ZIP Archive
echo.
echo Creating Portable ZIP Archive (%ARCH%)...
powershell -Command "if (Test-Path '%DIST%\Lertaro-Portable%ARCHSUFFIX%.zip') { Remove-Item -Force '%DIST%\Lertaro-Portable%ARCHSUFFIX%.zip' }; Compress-Archive -Path '%OUT%' -DestinationPath '%DIST%\Lertaro-Portable%ARCHSUFFIX%.zip' -Force"
if errorlevel 1 (
    echo [Error] Portable ZIP creation failed.
    exit /b 1
)

goto :eof
