# Indexing Management

The Indexing settings page controls indexing scopes, refresh schedules, and exclusion rules across local hard drives, network shares, WSL distributions, and standalone folders. Top tabs include: **Local Drives**, **Network Drives**, **WSL** (shown only when distributions are detected), **Folders**, and **Exclusions**.

## 1. Local Drives

- **Top Status Card**: Displays total indexed physical drives and item counts, with a global **Rebuild Index** button.
- **Drive Row Items**:
  - **Enable / Disable Checkbox**: Toggles indexing for each individual partition.
  - **Filesystem & Status**: Displays underlying filesystem (NTFS, ReFS, FAT32, exFAT), health state, and indexed count.
  - **Per-Drive Actions**: Individual **Rebuild** and **Remove** buttons; displays a dynamic **Stop** button during active scans.
- **Real-Time Tracking**: NTFS / ReFS volumes sync incrementally via Windows USN Change Journals; FAT32 / exFAT volumes monitor filesystem change events.
- **Uninterrupted Rebuilds**: While a drive is rebuilding, its existing index answers user queries until the new index completes and replaces it in one step. If interrupted, scanning resumes from the last checkpoint on the next launch.

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

Exclusion rules filter search results and bound how far the network, WSL and folder-index walks descend. They do not stop a local drive from being indexed: a local volume is always walked in full, so a path such as `E:\` in this list only hides its results. To stop indexing a local drive, turn it off under **Local Drives** above. Rules live in one list, with no sub-tabs. Every rule is a match plus a level; the match is decided by how the text is spelled, and the input box reports it live as **Detected:**.

- **Full path**: starts with a drive letter (`D:\Cache`), a UNC path (`\\server\share`), an environment variable (`%ProgramData%`, `%APPDATA%`), or a `shell:` virtual path. It covers that folder and everything under it.
- **Wildcard**: any other spelling. `*` matches within a single folder level (`*.log`), `?` matches one character, `**` matches recursively (`**\node_modules\**`). A rule without a separator matches by name at any depth (`node_modules`, `*.tmp`); a rule with separators is anchored at the drive root, so `Listary\abc` covers `C:\Listary\abc` and its contents but not `D:\work\Listary\abc`.
- **Regular expression**: wrap it in slashes, e.g. `/^\./` for hidden dotfiles or `/~\$/` for temporary Office locks. It runs as an implicit partial match against the full path or the file name. Prefer a wildcard where you can: a regex is interpreted on every match and costs more.

The **level** decides what happens: **Exclude** hides the item, while **Low**, **Normal**, and **High** only change the order, and a high rule can be pinned to put its rows at the very top. Set the level in the drop-down on the right of the row, or write it as a prefix in the text: `!` exclude, `-` low, `+` high, nothing means normal. A leading backslash makes the prefix character literal.

> [!NOTE]
> The prefix is only stripped in the **Add** box and the **Bulk edit** box. Inline editing of an existing rule keeps the text literal, so change the level with the drop-down: a `!` left inside a wildcard rule becomes a character of the path, and since Windows paths contain no `!`, such a rule can never match anything and the page shows no error for it.

Local drives apply the rules during search; network drives, WSL and custom folders skip excluded items while building the index and refresh after you apply. When an exclude rule covers a priority rule, a conflict banner appears above the list and the covered rule has no effect.

> [!TIP]
> Rules support both **single entry additions** and **batch editing**: click **Generate from List** to export rules to text, edit it, and click **Apply to List** to update in bulk. If one line of the batch text cannot be parsed, nothing is applied and the text stays in the box for you to fix.
