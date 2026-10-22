; =====================================================================
; Lertaro Inno Setup Script
; =====================================================================

#define AppName "Lertaro"
#define AppPublisher "Lertaro developer"
#define AppURL "https://lertaro.github.io/"
#define AppExeName "Lertaro.App.exe"
#define ServiceExeName "Lertaro.Service.exe"
#define ServiceName "LertaroService"
#define CliExeName "lff.exe"

; Architecture this installer is being built for, passed by make.bat as /DArch=x64 or /DArch=arm64.
; Defaults to x64 so compiling this script by hand still produces what it always produced.
#ifndef Arch
  #define Arch "x64"
#endif

; The x64 installer deliberately keeps its unsuffixed name. Existing installs and the release assets
; they update from are matched by name (see UpdateAssetSelector), so renaming it would strand them.
;
; The arm64 suffix uses an underscore rather than a hyphen to keep the portable zip it is paired with
; sorting after the x64 one: GitHub returns a release's assets sorted by name in byte order, '-' (0x2D)
; falls below '.' (0x2E), and installs predating UpdateAssetSelector take the first asset ending in
; ".zip". Only the zip name actually decides that, but both artifacts carry the same suffix so there is
; one convention to keep rather than two, and no hyphen sitting here to be copied back onto the zip.
#if Arch == "arm64"
  #define ArchSuffix "-arm64"
  #define SetupArchitectures "arm64"
  #define DotNetRuntimeFile "windowsdesktop-runtime-10-win-arm64.exe"
  #define DotNetRuntimeUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-arm64.exe"
  #define PublishDir "..\publish\arm64\Lertaro"
#else
  #define ArchSuffix ""
  #define SetupArchitectures "x64compatible"
  #define DotNetRuntimeFile "windowsdesktop-runtime-10-win-x64.exe"
  #define DotNetRuntimeUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"
  #define PublishDir "..\publish\x64\Lertaro"
#endif

[Setup]
AppId={{D37D0B75-B5E3-40D9-92EE-429C7D4D7F2A}
AppName={#AppName}
AppVersion={#AppVersion}
UninstallDisplayName={#AppName}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={commonpf64}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=Lertaro-Setup{#ArchSuffix}
SetupIconFile=..\App\logo.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed={#SetupArchitectures}
ArchitecturesInstallIn64BitMode={#SetupArchitectures}
#if Arch == "x64"
SetupArchitecture=x64
#endif
PrivilegesRequired=admin
VersionInfoVersion={#AppVersion4}
VersionInfoTextVersion={#AppVersion}
; Automatically check and close running instances of the App (and the lff CLI companion, which sits
; in the same install directory and can otherwise hold its own exe/dll files locked)
CloseApplications=yes
CloseApplicationsFilter={#AppExeName},{#CliExeName}

[Languages]
Name: "en_US"; MessagesFile: "compiler:Default.isl"
Name: "zh_CN"; MessagesFile: "ThirdParty\ChineseSimplified.isl"
Name: "zh_TW"; MessagesFile: "ThirdParty\ChineseTraditional.isl"
Name: "zh_HK"; MessagesFile: "ThirdParty\ChineseTraditional.isl"
Name: "es_ES"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "ja_JP"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "ko_KR"; MessagesFile: "compiler:Languages\Korean.isl"

#include "Languages\en-US.iss"
#include "Languages\zh-CN.iss"
#include "Languages\zh-TW.iss"
#include "Languages\zh-HK.iss"
#include "Languages\es-ES.iss"
#include "Languages\ja-JP.iss"
#include "Languages\ko-KR.iss"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startmenuicon"; Description: "{cm:CreateStartMenuIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "addtopath"; Description: "{cm:AddLffToPath}"; GroupDescription: "{cm:CommandLineTools}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{commonprograms}\{#AppName}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: startmenuicon
Name: "{commonprograms}\{#AppName}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"; Tasks: startmenuicon
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Run the app as original non-elevated user at the end
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchApp}"; Flags: postinstall nowait runasoriginaluser

; Service stop/delete on uninstall is handled in CurUninstallStepChanged below (which also kills the
; app and hook process first), so no [UninstallRun] entries are needed.

[Code]
var
  DownloadPage: TDownloadWizardPage;

const
  EnvironmentKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';
  WindowsNtCurrentVersionKey = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion';
  // Windows 10 22H2 is the newest build that lacks user-mode CET
  LastWindowsBuildWithoutUserCet = 19045;

// Adds Dir to the MACHINE-wide PATH (HKLM, not HKCU) -- the installer already runs elevated
// (PrivilegesRequired=admin above) and everything else it installs (Program Files location, the
// Windows Service) is machine-wide too, so a per-user PATH entry would be the odd one out here. Reads
// with the raw (non-expanded) string so an existing entry like "%SystemRoot%\..." isn't baked into a
// literal path, and writes back as REG_EXPAND_SZ (RegWriteExpandStringValue, not
// RegWriteStringValue) for the same reason -- downgrading the whole value's type would break expansion
// for every OTHER entry already in there, not just whatever this adds. No-ops if Dir is already present
// (checked as ;DIR; against ;PATH; so it can't false-match a longer path sharing the same prefix).
procedure EnvAddPath(Dir: string);
var
  Paths: string;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths) then
    Paths := '';

  if Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Paths) + ';') > 0 then
    exit;

  if (Length(Paths) > 0) and (Paths[Length(Paths)] <> ';') then
    Paths := Paths + ';';
  Paths := Paths + Dir;

  if not RegWriteExpandStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths) then
    Log('Failed to add ' + Dir + ' to the machine PATH.');
end;

// Mirror of EnvAddPath -- same registry/type reasoning applies. Always attempted on uninstall
// regardless of whether the "addtopath" task was originally selected (task selections aren't
// remembered across a separate uninstall run) -- harmless no-op via the same Pos check above if Dir
// was never actually added.
procedure EnvRemovePath(Dir: string);
var
  Paths: string;
  P: Integer;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths) then
    exit;

  P := Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Paths) + ';');
  if P = 0 then
    exit;

  Delete(Paths, P - 1, Length(Dir) + 1);

  if not RegWriteExpandStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths) then
    Log('Failed to remove ' + Dir + ' from the machine PATH.');
end;

function IsDotNet10Installed(): Boolean;
var
  FindRec: TFindRec;
  Path: string;
begin
  Result := False;
  // Which directory holds "the runtime this build needs" is not fixed: on an arm64 machine the arm64
  // runtime installs under dotnet\ and the x64 one under dotnet\x64\. This installer is allowed to run
  // on arm64 (x64compatible), so the x64 build has to look in the x64 subdirectory there -- checking
  // dotnet\ would find the ARM64 runtime, conclude everything was in place, and skip an install the
  // x64 app cannot start without.
  if IsArm64 and ('{#Arch}' = 'x64') then
    Path := ExpandConstant('{commonpf64}\dotnet\x64\shared\Microsoft.WindowsDesktop.App')
  else
    Path := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if FindFirst(Path + '\10.*', FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

// Windows 10 22H2 (build 19045) and older never finished user-mode CET support, while the .NET SDK has
// marked every apphost /CETCOMPAT since .NET 9. On a CPU with hardware CET (Intel 12th gen / AMD Zen 3
// and newer) coreclr then aborts inside EEStartup with "Your Windows doesn't fully support CET. Please
// install all available Windows updates" before a single line of managed code runs, so the app closes
// within a second of launching and leaves no Lertaro log behind.
// Clearing the user-mode shadow stack for our executables is the supported per-app workaround. That
// policy belongs to Set-ProcessMitigation (it writes an Image File Execution Options value that is not
// meant to be hand-built), and passing the full installed path rather than a bare file name scopes the
// loosening to this installation instead of every process named lff.exe on the machine.
procedure DisableUserShadowStackOnOldWindows;
var
  Build: string;
  BuildNumber: Integer;
  Command: string;
  Exe: string;
  I: Integer;
  ResultCode: Integer;
  ExeNames: array[0..2] of string;
begin
  // CurrentBuildNumber is the Windows 10+ name, CurrentBuild the older one. A build that can't be read
  // falls through to the mitigation: skipping it costs a customer who cannot launch the app, while
  // applying it on a machine without user-mode CET costs nothing.
  if RegQueryStringValue(HKEY_LOCAL_MACHINE, WindowsNtCurrentVersionKey, 'CurrentBuildNumber', Build) or
     RegQueryStringValue(HKEY_LOCAL_MACHINE, WindowsNtCurrentVersionKey, 'CurrentBuild', Build) then
    BuildNumber := StrToIntDef(Build, 0)
  else
    BuildNumber := 0;

  // Windows 11 starts at build 22000, so this is exactly "Windows 10 22H2 and older"
  if BuildNumber > LastWindowsBuildWithoutUserCet then
    exit;

  ExeNames[0] := '{#AppExeName}';
  ExeNames[1] := '{#ServiceExeName}';
  ExeNames[2] := '{#CliExeName}';
  for I := 0 to High(ExeNames) do
  begin
    Exe := ExpandConstant('{app}\' + ExeNames[I]);
    // Set-ProcessMitigation refuses a path that doesn't exist yet, which is why this waits for the
    // file copy instead of running in PrepareToInstall like the process kills do.
    if FileExists(Exe) then
    begin
      // {app} is user-editable text, and a PowerShell single-quoted string escapes a quote by doubling
      StringChangeEx(Exe, '''', '''''', False);
      Command := Command + 'Set-ProcessMitigation -Name ''' + Exe + ''' -Disable UserShadowStack; ';
    end;
  end;

  if Command = '' then
    exit;

  // Logged, not shown: a machine without PowerShell (or with it blocked by policy) can't be repaired
  // from here, and the same commands are written out in the troubleshooting guide for that case.
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
       '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' + Command + '"', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    Log('Could not disable the user-mode shadow stack for ' + ExpandConstant('{app}') +
      ' (PowerShell exit code ' + IntToStr(ResultCode) + '). Lertaro may still fail to start with' +
      ' "Your Windows doesn''t fully support CET"; run the commands from the troubleshooting guide.');
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), CustomMessage('DotNetDownloading'), @OnDownloadProgress);
end;

function PrepareToInstall(var NeedsReboot: Boolean): String;
var
  ResultCode: Integer;
  InstallerPath: string;
begin
  Result := '';

  // Inno doesn't switch away from the interactive Ready page (whose Install/Back buttons stay
  // enabled) until this function returns -- without disabling them explicitly, a user can click
  // Install again (re-entering this function) or Back while the .NET download/silent-install below
  // is still running, which is exactly what was happening. Cancel is left alone so a stuck download
  // can still be aborted. try/finally guarantees these get re-enabled on every exit path, including
  // the early Exit on a failed download.
  WizardForm.NextButton.Enabled := False;
  WizardForm.BackButton.Enabled := False;
  try
    // 1. Force stop the service before installing new files (deleting it is only done on uninstall,
    // see CurUninstallStepChanged below), and any running copy of the CLI companion -- it sits in the
    // same install directory as everything else here, so a locked lff.exe/lff.dll can block the file
    // copy below just as easily as a locked App/Service file would.
    Exec('sc.exe', 'stop ' + '{#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/F /IM ' + '{#ServiceExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/F /IM ' + '{#CliExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // 2. Check and Download .NET 10.0 Desktop Runtime if missing
    if not IsDotNet10Installed() then
    begin
      DownloadPage.Clear;
      DownloadPage.Add('{#DotNetRuntimeUrl}', '{#DotNetRuntimeFile}', '');
      DownloadPage.Show;
      try
        try
          DownloadPage.Download;
        except
          Result := CustomMessage('DotNetDownloadFailed');
          Exit;
        end;
      finally
        DownloadPage.Hide;
      end;

      // DownloadPage.Hide switches the wizard back to the Ready page underneath it, and Inno's own
      // page-switch logic resets that page's Next/Back to their normal (enabled) state as part of
      // showing it again -- silently undoing the disable above. Re-assert it for the silent runtime
      // install that follows, which is exactly the phase where the buttons were still clickable.
      WizardForm.NextButton.Enabled := False;
      WizardForm.BackButton.Enabled := False;

      // Install the downloaded runtime
      WizardForm.StatusLabel.Caption := CustomMessage('DotNetInstalling');
      InstallerPath := ExpandConstant('{tmp}\{#DotNetRuntimeFile}');
      if not Exec(InstallerPath, '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
      begin
        Result := FmtMessage(CustomMessage('DotNetInstallFailed'), [IntToStr(ResultCode)]);
      end;
    end;
  finally
    WizardForm.NextButton.Enabled := True;
    WizardForm.BackButton.Enabled := True;
  end;
end;

procedure StartServiceIfRegistered;
var
  ResultCode: Integer;
begin
  // PrepareToInstall stops and kills the service so its files can be replaced, and nothing started it
  // again: until it runs, the App that [Run] launches has no index and no updater until it happens to
  // start the service itself. Only when the machine ever registered it -- an installation whose App was
  // never run has no service, and `sc start` on that is just error 1060.
  if RegKeyExists(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Services\{#ServiceName}') then
    if not Exec('sc.exe', 'start ' + '{#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Log('Could not run sc.exe to start the service.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('addtopath') then
      EnvAddPath(ExpandConstant('{app}'));
    DisableUserShadowStackOnOldWindows;
    StartServiceIfRegistered;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Force stop app, CLI companion, and service on uninstallation
    Exec('taskkill.exe', '/F /IM ' + '{#AppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/F /IM ' + '{#CliExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc.exe', 'stop ' + '{#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/F /IM ' + '{#ServiceExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc.exe', 'delete ' + '{#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  // Always attempted (see EnvRemovePath's own comment on why) -- after files are gone (usPostUninstall),
  // not usUninstall (which the block above uses to stop processes still holding files open).
  if CurUninstallStep = usPostUninstall then
  begin
    EnvRemovePath(ExpandConstant('{app}'));

    // Both are HKCU writes the running app itself makes (StartupManager.cs / UrlProtocolManager.cs),
    // not the installer -- so the installer is the only place left to clean them up. RegDeleteValue
    // only removes Lertaro's own value from the shared Run key (never the key itself, other apps use
    // it too); RegDeleteKeyIncludingSubkeys removes the whole lertaro:// protocol registration tree.
    // Both no-op harmlessly if already absent.
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Lertaro');
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\lertaro');
  end;
end;
