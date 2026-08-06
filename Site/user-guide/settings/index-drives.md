# Index

Five tabs, in order: **Local Drives**, **Network Drives**, **WSL** (only shown once a distribution is detected), **Folders**, and **Exclusion Rules**.

## Local Drives

- Status card summarizing how many drives and items are indexed, plus a **Rebuild Index** button for a full re-scan.
- One row per local drive: an **enable/disable checkbox**, drive name, file system (NTFS/ReFS/...), current status, indexed item count, and a per-row **Rebuild**/**Remove** action — plus a **Stop** action while a rebuild is running, for every drive except true NTFS (its scan has no safe point to interrupt).
- NTFS and ReFS drives track changes continuously through the Windows USN Journal; other local file systems (FAT32, exFAT, ...) have no journal to read, so they're watched for changes directly instead. Either way, a manual rebuild is rarely needed, but is there if something looks out of sync — and if one gets interrupted (stopped, or the app/service restarts mid-scan), the next rebuild picks up from where it left off instead of starting over.

Searching keeps working while a rebuild is running. The drive being rebuilt goes on answering from its previous index until the new one is ready, and the other drives are unaffected — a rebuild never leaves you with an empty result list.

## Network Drives

- Same status card and **Rebuild Index** button as Local Drives.
- One row per mapped network drive: enable checkbox, path/name, status (Indexing / Ready / Cached / Failed / Pending / Connected), item count, and a **Refresh Mode** dropdown:
  - **Manual** — only refreshes on demand (via Rebuild Index).
  - **Every 15 minutes**
  - **Every hour**
  - **Daily**

Network shares don't expose a change journal the way local NTFS volumes do, which is why they're refreshed on a schedule instead of in real time. The scanning engine includes built-in branch path tracking and symlink loop detection, automatically intercepting recursion loops on NAS/SMB shares even if symlinks point back to ancestor directories.

## WSL

Only shown once at least one WSL distribution is detected — same shape as Network Drives (status card, **Rebuild Index** button, and one row per distribution with status/item count/**Refresh Mode**). Distributions are detected automatically; there's no manual "add" step.

## Folders

Index arbitrary individual folders instead of a whole drive or share — useful for indexing just one subtree without pulling in everything else on that volume.

- An **Add Folder** button opens a folder picker; a **Rebuild Index** button re-scans every folder in the list.
- One row per added folder: enable checkbox, path, status, item count, and the same **Refresh Mode** dropdown (Manual / Every 15 minutes / Every hour / Daily) as network drives — folders are scanned on a schedule rather than tracked continuously, the same way network shares are.
- The folder picker also accepts a **UNC network share path** (e.g. `\\server\share`, or a subfolder inside it) browsed to via *Network* in the picker — useful for indexing a single share or subtree without adding the whole server as a mapped network drive.

## Exclusion Rules

Three sub-tabs, each with the same shape: a single-entry textbox + **Add** button, a list of existing rules (each editable/deletable), and a bulk multi-line textbox with **Generate from List** / **Apply to List** buttons for editing everything at once.

- **Path Exclusions** — full paths or environment variables (e.g. `D:\Cache`, `%ProgramData%`).
- **Glob Wildcards** — `*` (any characters in a filename), `?` (single character), `**` (recursive directories). Examples: `*.tmp`, `**/node_modules/**`, `bin/**`.
- **Regex Patterns** — arbitrary regular expressions matched against the path/filename (partial match). Examples: `^\.` (hidden files), `~$` (Office temp files), `\.git\`.

Exclusions apply to local, network, and folder indexing alike, and network drives/folders re-scan automatically after you apply exclusion changes.
