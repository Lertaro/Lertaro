# Indexing Management

The Indexing settings page controls indexing scopes, refresh schedules, and exclusion rules across local hard drives, network shares, WSL distributions, and standalone folders. Top tabs include: **Local Drives**, **Network Drives**, **WSL** (shown only when distributions are detected), **Folders**, and **Exclusions**.

## 1. Local Drives

- **Top Status Card**: Displays total indexed physical drives and item counts, with a global **Rebuild Index** button.
- **Drive Row Items**:
  - **Enable / Disable Checkbox**: Toggles indexing for each individual partition.
  - **Filesystem & Status**: Displays underlying filesystem (NTFS, ReFS, FAT32, exFAT), health state, and indexed count.
  - **Per-Drive Actions**: Individual **Rebuild** and **Remove** buttons; displays a dynamic **Stop** button during active scans.
- **Real-Time Tracking**: NTFS / ReFS volumes sync incrementally via Windows USN Change Journals; FAT32 / exFAT volumes monitor filesystem change events.
- **Uninterrupted Rebuilds**: While a drive is rebuilding, its existing index answers user queries until the new index completes, swapping seamlessly. If interrupted, scanning resumes from the last checkpoint on the next launch.

## 2. Network Drives

- **Network Storage Support**: Lists all mapped Windows network drives (SMB / NAS).
- **Refresh Mode**: Network shares lack local USN journals and rely on scheduled polling:
  - **Manual** — Refreshes only when "Rebuild Index" is clicked.
  - **Every 15 Minutes** — Recommended for active collaborative shares.
  - **Hourly** — Balanced schedule.
  - **Daily** — Ideal for static archival storage.
- **Symlink Loop Protection**: Built-in cycle detection and ancestor stack algorithms automatically prevent infinite traversal caused by circular symbolic links or reparse points.

## 3. WSL (Windows Subsystem for Linux)

Appears automatically whenever at least one WSL distribution is installed:

- **Auto-Discovery**: Automatically recognizes Ubuntu, Debian, Arch, and other WSL distros.
- **Status & Schedules**: Identical status cards and Refresh Mode options (Manual / 15 Min / Hourly / Daily).
- **Zero-I/O Queries**: WSL searches read the in-memory tree directly without waking or blocking the Linux subsystem.

## 4. Folders

Use standalone folder indexing when you want to target specific working directories instead of entire volumes or NAS shares:

- **Multi-Select Addition**: Click **Add Folder** to open a native picker supporting `Ctrl` / `Shift` batch selection.
- **UNC Path Support**: Add network UNC share paths directly (e.g. `\\server\share\projects`).
- **Independent Schedules**: Each folder has its own toggle, item counter, and Refresh Mode dropdown.

## 5. Exclusions

Exclusion rules apply globally across local drives, network storage, and custom folders, organized into three sub-tabs:

### Path Exclusions

- **Matching Logic**: Prefix matching against absolute physical paths.
- **Environment Variables**: Accepts `%ProgramData%`, `%APPDATA%`, or `D:\Cache`.

### Glob Rules

- **Syntax**:
  - `*`: Matches characters within a single folder level (e.g. `*.tmp`, `*.log`).
  - `**`: Matches recursively across subdirectories (e.g. `**/node_modules/**`, `**/bin/**`, `**/obj/**`).

### Regex Rules

- **Advanced Filtering**: Regular expression matching against paths and filenames (e.g. `^\.` for hidden dotfiles, `~\$` for temporary Office locks).

> [!TIP]
> Exclusions support both **single entry additions** and **batch editing**: click **Generate from List** to export rules to text, edit, and click **Apply to List** to update in bulk. Modified exclusion rules automatically trigger index filtering.
