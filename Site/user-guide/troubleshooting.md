# Troubleshooting

Encountering issues while using Lertaro? Follow these systematic steps to diagnose and resolve common situations.

## 1. Global Hotkeys Not Responding

- **Verify Background Service Status**: Go to [**Settings → Service Status**](./settings/service-status) and verify that `Lertaro.Service` is running. While general hotkeys are handled by the App, the elevated hook process depends on the background service.
- **Administrative Privilege Isolation (UIPI)**: If the active foreground window is elevated (e.g. Administrator Terminal or Task Manager), Windows UIPI prevents standard-user processes from intercepting hotkeys. Lertaro automatically elevates its keyboard hook process via the service; ensure the service is running.
- **Check Process Blacklist**: In [**Settings → Hotkeys**](./settings/hotkeys-page#process-blacklist), ensure the active application is not listed in the Process Blacklist. Blacklisted apps cause Lertaro to bypass global hotkeys intentionally.
- **Fullscreen App Bypass**: When an application is running in exclusive fullscreen mode (e.g. 3D games or video players), Lertaro suppresses hotkeys by default. Enable **Respond when focused on full-screen applications** under **Settings → Hotkeys** if you wish to override this behavior.

## 2. Search Results Stale or Incomplete

- **Local NTFS / ReFS Drives**: Updated in real time with near-zero latency by monitoring the filesystem's USN Change Journal.
- **FAT32 / exFAT Drives**: Tracked continuously via filesystem change events.
- **Manual Reindex**: If an unexpected crash or sudden power cut caused index discrepancies, go to [**Settings → Indexing → Local Drives**](./settings/index-drives) and click **Rebuild Index** on the affected drive.

## 3. Network Drives Not Refreshing

- **Scheduled Rescan**: Network shares (SMB / NAS) lack local USN journals and rely on scheduled polling.
- **Check Refresh Mode**: In [**Settings → Indexing → Network Drives**](./settings/index-drives#network-drives), check if the refresh mode is set to "Manual". If so, switch to a periodic schedule or click "Rebuild Index".
- **Symlink Loop Protection**: Lertaro includes a built-in cycle detection engine to prevent infinite loops caused by recursive symbolic links on NAS shares.

## 4. Specific Files or Folders Missing

- **Review Exclusion Rules**: Go to [**Settings → Indexing → Exclusions**](./settings/index-drives#exclusion-rules) and verify that the path is not inadvertently matched by exact path, glob, or regex patterns.
- **Verify Drive Status**: In [**Settings → Indexing → Local Drives**](./settings/index-drives), confirm that the drive or mount point containing the file is marked as enabled.

## 5. IME Candidate Window Not Appearing in Inline Window

- **Non-Focus Design**: The [Inline Window](./getting-started#3-three-window-modes) intentionally avoids stealing real keyboard focus from the host application so it can dismiss cleanly without UI flicker. Because IME candidate popups require true window focus, they may not render in inline mode.
- **Recommended Solutions**:
  1. **Direct Pinyin Typing**: Lertaro features an embedded pinyin alias engine; type pinyin letters directly to fuzzy-match Chinese filenames without opening an IME popup (see [**Search Syntax**](./search-syntax#8-multilingual--pinyin-aliases)).
  2. **Switch to Quick Window**: Double-tap `Ctrl` to open the fully focused Quick Window, where all input methods work normally.

## 6. Inspecting Logs & Submitting Issues

If the problem persists, visit [**Settings → Service Status**](./settings/service-status) to inspect live logs:

- **Service Logs**: Records background indexing, USN journals, network scans, and IPC communication.
- **App Logs**: Records foreground UI rendering, plugin lifecycles, and configuration updates.
- **Hook Logs**: Records global keyboard hook and mouse gesture event streams.

Use the search bar to filter keywords or sort by severity (Info / Warn / Error) to locate stack traces before attaching logs to a GitHub issue.
