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

:: 2. Clean dist/ (the per-architecture publish dirs are cleaned in :build_arch)
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
set "ISCC="

if exist "C:\Program Files\Inno Setup 7\ISCC.exe" (
    set "ISCC=C:\Program Files\Inno Setup 7\ISCC.exe"
    goto :iscc_found
)

if exist "%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe" (
    set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe"
    goto :iscc_found
)

:iscc_not_found
echo [Error] Inno Setup 7 compiler (ISCC.exe) not found.
echo Checked paths:
echo   1. C:\Program Files\Inno Setup 7\ISCC.exe
echo   2. %LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe
echo.
echo Please install Inno Setup 7.
exit /b 1

:iscc_found
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

:: 5. Materialize the signing key
:: LERTARO_SIGNING_KEY is the key file as ONE base64 line, the contract the GitLab job and the GitHub
:: workflow share. The sweep below clears the leftovers of a run that died halfway.
echo.
echo Decoding the signing key...
if not defined LERTARO_SIGNING_KEY (
    echo [Error] LERTARO_SIGNING_KEY is not set. It must hold the key file as one base64 line, for example
    echo         the output of:  openssl base64 -A -in lertaro-release.key
    exit /b 1
)
del /q "%TEMP%\lertaro-release-*.key" >nul 2>&1
set "SIGN_KEY=%TEMP%\lertaro-release-%RANDOM%.key"
powershell -NoProfile -Command "[IO.File]::WriteAllBytes($env:SIGN_KEY,[Convert]::FromBase64String(($env:LERTARO_SIGNING_KEY -replace '\s','')))"
if not exist "%SIGN_KEY%" (
    echo [Error] LERTARO_SIGNING_KEY is not base64 of a key file; nothing could be decoded.
    exit /b 1
)


:: Two architectures, same steps, and separate artifacts rather than one combined installer: BrowserData
:: carries a native library and only one architecture's copy fits its flat, deps.json-less load directory.
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
:: Conventional hyphenated suffix, which legacy installs can see; in step with UpdateAssetSelector.SuffixFor and installer.iss.
set "ARCHSUFFIX=-arm64"
set "RIDARGS=-r win-arm64 --self-contained false"
call :build_arch
if errorlevel 1 exit /b 1

:: 6. Clean up temporary publish folder and the decoded key
echo.
echo Cleaning up temporary publish folder...
rmdir /s /q "%ROOT%publish" >nul 2>&1
del /q "%SIGN_KEY%" >nul 2>&1

echo.
echo ==========================================
echo Build and Package Completed Successfully!
echo Installer (x64):     %DIST%\Lertaro-Setup.exe
echo Portable ZIP (x64):  %DIST%\Lertaro-Portable.zip
echo Installer (arm64):   %DIST%\Lertaro-Setup-arm64.exe
echo Portable ZIP (arm64):%DIST%\Lertaro-Portable-arm64.zip
echo ==========================================
exit /b 0


:: Per-architecture build, driven by ARCH / ARCHSUFFIX / RIDARGS set by the caller rather than by
:: parameters: an empty argument (x64 passes no RID) is awkward to quote in batch.
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

:: One publish per project, in the order Lertaro.slnx lists them: -o on a solution is NETSDK1194's
:: unsupported shape, where every project writes into the same folder in whatever order MSBuild picks.
:: The list is read from the solution file, so it cannot drift from it.
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
copy "%ROOT%portable-updater.ps1" "%OUT%\" >nul
if errorlevel 1 exit /b 1
copy "%ROOT%install-dotnet-runtime.bat" "%OUT%\" >nul
if errorlevel 1 (
    echo [Warning] Failed to copy install-dotnet-runtime.bat.
)
copy "%ROOT%portable-cleanup.bat" "%OUT%\" >nul
if errorlevel 1 (
    echo [Warning] Failed to copy portable-cleanup.bat.
)
del /s /q "%OUT%\*.pdb" >nul 2>&1

echo.
echo [4/4] Compiling Inno Setup Installer...
echo Using Inno Setup compiler: "%ISCC%"
"%ISCC%" /DAppVersion="%APP_VER%" /DAppVersion4="%APP_VER_4%" /DArch="%ARCH%" "%ROOT%Installer\installer.iss"
if errorlevel 1 (
    echo [Error] Inno Setup compilation failed for %ARCH%.
    exit /b 1
)

echo.
echo Creating Portable ZIP Archive (%ARCH%)...
powershell -Command "if (Test-Path '%DIST%\Lertaro-Portable%ARCHSUFFIX%.zip') { Remove-Item -Force '%DIST%\Lertaro-Portable%ARCHSUFFIX%.zip' }; Compress-Archive -Path '%OUT%' -DestinationPath '%DIST%\Lertaro-Portable%ARCHSUFFIX%.zip' -Force"
if errorlevel 1 (
    echo [Error] Portable ZIP creation failed.
    exit /b 1
)

:: Sign the portable ZIP: the updater verifies an ECDSA P-256 signature over the whole archive and refuses
:: an unsigned package, so signing belongs here rather than to a separate release step.
set "SIGN_ZIP=%DIST%\Lertaro-Portable%ARCHSUFFIX%.zip"

echo Signing %SIGN_ZIP%...
:: OpenSSL is preferred: its output can be checked with the command published in the release notes. It is
:: not always on PATH, so the two usual Windows install locations are probed too.
set "OPENSSL="
where openssl >nul 2>&1
if not errorlevel 1 set "OPENSSL=openssl"
if not defined OPENSSL if exist "%ProgramFiles%\OpenSSL-Win64\bin\openssl.exe" set "OPENSSL=%ProgramFiles%\OpenSSL-Win64\bin\openssl.exe"
if not defined OPENSSL if exist "%ProgramFiles%\Git\usr\bin\openssl.exe" set "OPENSSL=%ProgramFiles%\Git\usr\bin\openssl.exe"
if defined OPENSSL (
    "%OPENSSL%" dgst -sha256 -sign "%SIGN_KEY%" -out "%SIGN_ZIP%.sig" "%SIGN_ZIP%"
    if errorlevel 1 (
        echo [Error] Signing %SIGN_ZIP% failed.
        exit /b 1
    )
    goto :eof
)

:: No OpenSSL anywhere: fall back to the same ECDsa code the app verifies with. That needs PowerShell 7,
:: since Windows PowerShell's .NET Framework has no ImportFromPem.
set "LERTARO_SIGN_KEY=%SIGN_KEY%"
set "LERTARO_SIGN_ZIP=%SIGN_ZIP%"
where pwsh >nul 2>&1
if errorlevel 1 (
    echo [Error] Neither OpenSSL nor PowerShell 7 is available to sign %SIGN_ZIP%.
    exit /b 1
)
pwsh -NoProfile -Command "$ErrorActionPreference='Stop'; $k=[System.Security.Cryptography.ECDsa]::Create(); $k.ImportFromPem([System.IO.File]::ReadAllText($env:LERTARO_SIGN_KEY)); $s=$k.SignData([System.IO.File]::ReadAllBytes($env:LERTARO_SIGN_ZIP),[System.Security.Cryptography.HashAlgorithmName]::SHA256,[System.Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence); if (-not $k.VerifyData([System.IO.File]::ReadAllBytes($env:LERTARO_SIGN_ZIP),$s,[System.Security.Cryptography.HashAlgorithmName]::SHA256,[System.Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)) { throw 'self-verification of the new signature failed' }; [System.IO.File]::WriteAllBytes($env:LERTARO_SIGN_ZIP+'.sig',$s)"
if errorlevel 1 (
    echo [Error] Signing %SIGN_ZIP% failed.
    exit /b 1
)
:: Without ErrorActionPreference these .NET calls fail as non-terminating errors: pwsh printed the exception,
:: still wrote the file, exited 0, and left a blob that verifies against nothing -- hence the check below.
if not exist "%SIGN_ZIP%.sig" (
    echo [Error] Signing %SIGN_ZIP% produced no signature file.
    exit /b 1
)

goto :eof
