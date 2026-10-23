# Troubleshooting

Encountering issues while using Lertaro? Follow these systematic steps to diagnose and resolve common situations.

## 1. Global Hotkeys Not Responding

- **Verify Background Service Status**: Go to [**Settings → Service Status**](./settings/service-status) and verify that the background indexing service is running. While general hotkeys are handled by the App, the elevated hook process depends on the background service.
- **Administrative Privilege Isolation (UIPI)**: If the active foreground window is elevated (e.g. Administrator Terminal or Task Manager), Windows UIPI prevents standard-user processes from intercepting hotkeys. Lertaro automatically elevates its keyboard hook process via the service; ensure the service is running.
- **Check Process Blacklist**: In [**Settings → Hotkeys**](./settings/hotkeys-page#_3-process-blacklist), ensure the active application is not listed in the Process Blacklist. Blacklisted apps cause Lertaro to bypass global hotkeys intentionally.
- **Fullscreen App Bypass**: When an application is running in exclusive fullscreen mode (e.g. 3D games or video players), Lertaro suppresses hotkeys by default. Enable **Respond when focused on full-screen applications** under **Settings → Hotkeys** if you wish to override this behavior.

## 2. Search Results Stale or Incomplete

- **Local NTFS / ReFS Drives**: Updated in real time with near-zero latency by monitoring the filesystem's USN Change Journal.
- **FAT32 / exFAT Drives**: Tracked continuously via filesystem change events.
- **Manual Reindex**: If an unexpected crash or sudden power cut caused index discrepancies, go to [**Settings → Indexing → Local Drives**](./settings/index-drives) and click **Rebuild Index** on the affected drive.

## 3. Network Drives Not Refreshing

- **Scheduled Rescan**: Network shares (SMB / NAS) lack local USN journals and rely on scheduled polling.
- **Check Refresh Mode**: In [**Settings → Indexing → Network Drives**](./settings/index-drives#_2-network-drives), check if the refresh mode is set to "Manual". If so, switch to a periodic schedule or click "Rebuild Index".
- **Symlink Loop Protection**: Lertaro includes a built-in cycle detection engine to prevent infinite loops caused by recursive symbolic links on NAS shares.

## 4. Specific Files or Folders Missing

- **Review Exclusion Rules**: Go to [**Settings → Indexing → Exclusions**](./settings/index-drives#_5-exclusions) and verify that the path is not inadvertently matched by exact path, glob, or regex patterns.
- **Verify Drive Status**: In [**Settings → Indexing → Local Drives**](./settings/index-drives), confirm that the drive or mount point containing the file is marked as enabled.

## 5. IME Candidate Window Not Appearing in Inline Window

- **Non-Focus Design**: The [Inline Window](./getting-started#_3-three-window-modes) intentionally avoids stealing real keyboard focus from the host application so it can dismiss cleanly without UI flicker. Because IME candidate popups require true window focus, they may not render in inline mode.
- **Recommended Solutions**:
  1. **Direct Pinyin Typing**: Lertaro features an embedded pinyin alias engine; type pinyin letters directly to fuzzy-match Chinese filenames without opening an IME popup (see [**Search Syntax**](./search-syntax#_7-multilingual-aliases)).
  2. **Switch to Quick Window**: Double-tap `Ctrl` to open the fully focused Quick Window, where all input methods work normally.

## 6. Data Directory, Permissions & Failed Updates

- **Relocating the data directory**: Moving `%LocalAppData%\Lertaro` (installer version) or the portable `Data\Users` folder to another partition with a symlink or junction is supported — the link target must be a **local fixed volume**. A link that points at a network location or a removable volume is refused at startup with the reason shown (see [**Settings → About**](./settings/about)).
- **Startup refused: the data folder belongs to another identity**: If the owner of the user data directory is not your account, Lertaro refuses to start and first attempts an elevated repair. The message names the **actual owner and the expected SID**, which is the whole diagnosis. Every step of that repair — including the step it was still on when it timed out — is written to `%TEMP%\Lertaro\repair.log`; attach that file when reporting the problem.

If the folder's owner is `BUILTIN\Administrators` while its permissions already grant *you* FullControl, reset the owner only. The script below changes ownership inside `%LOCALAPPDATA%\Lertaro` and nothing else: it does not touch permissions, and it does not touch file contents.

```powershell
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
$root = Join-Path $env:LOCALAPPDATA 'Lertaro'
Get-Item -LiteralPath $root | ForEach-Object {
    $acl = $_.GetAccessControl('Owner')
    $acl.SetOwner($sid)
    $_.SetAccessControl($acl)
}
Get-ChildItem -LiteralPath $root -Recurse -Force | ForEach-Object {
    $acl = $_.GetAccessControl('Owner')
    $acl.SetOwner($sid)
    $_.SetAccessControl($acl)
}
```

- **The last silent update failed**: The updater writes its verdict — status, reason and rollback directory — to `update-result.json` next to the installation. Lertaro shows that reason once on the next start and does not repeat the same verdict; if the notice keeps appearing, delete that file or install the new version manually over the current installation.
- **Links below the data root are always refused**: Only the data directory's own chain may be resolved as a link; subdirectories inside it may not be links.

## 7. The App Closes Immediately After Launch

- **Install finished, no window ever appears**: On Windows 10 22H2 (build 19045) and older, a CPU with hardware CET (Intel 12th generation or newer, AMD Zen 3 or newer) makes .NET abort before Lertaro's own code runs, because those builds don't fully support user-mode CET. The Event Viewer records a **.NET Runtime** entry reading *"Your Windows doesn't fully support CET. Please install all available Windows updates"*, and no Lertaro log is written at all — the process dies before logging begins.
- **Installer builds fix this automatically**: Setup reads the Windows build number and, on Windows 10 22H2 and older, clears the user-mode shadow-stack policy for the three executables in that install folder. Nothing else on the machine is affected, and re-running Setup re-applies it.
- **Portable builds**: Setup can't reach a portable folder, so open PowerShell as Administrator, replace the path with your own folder, and run these three commands once.

```powershell
Set-ProcessMitigation -Name 'D:\Lertaro\Lertaro.App.exe' -Disable UserShadowStack
Set-ProcessMitigation -Name 'D:\Lertaro\Lertaro.Service.exe' -Disable UserShadowStack
Set-ProcessMitigation -Name 'D:\Lertaro\lff.exe' -Disable UserShadowStack
```

- **Applying every available Windows update** is the other remedy, and is what the .NET message above asks for. If the app still closes immediately afterwards, collect the logs as described in the next section and open an issue.

## 8. Unexpected Search Order

In Quick Search, run the query, then right-click any result and choose **Export this page's ranking details**. Lertaro writes one plaintext `.txt` for the whole displayed page; no result needs to be opened. The export action is shown when **Settings → General → System → Enable ranking details export** is on (the default). Turning it off also stops building the explanation in memory.

Nothing is logged in the background. Review the exported file before sharing it: the query, result names, and paths are in plain text.

## 9. Inspecting Logs & Submitting Issues

If the problem persists, visit [**Settings → Service Status**](./settings/service-status) to inspect live logs:

- **Service Logs**: Records background indexing, USN journals, network scans, and IPC communication.
- **App Logs**: Records foreground UI rendering, plugin lifecycles, and configuration updates.
- **Hook Logs**: Records global keyboard hook and mouse gesture event streams.

Use the search bar to filter keywords or sort by severity (Info / Warn / Error) to locate stack traces before attaching logs to a GitHub issue.
