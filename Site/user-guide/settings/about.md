# About

Shows version numbers for the App, Core, Service, and CLI components (colored to reflect whether
the service is currently healthy), a short description of Lertaro, and links to the project
homepage and the online user guide.

## Data folders

Two more links, right below those, open the folders Lertaro reads and writes its own
configuration from — each shows the actual path as the clickable link text, creating the folder
first if it doesn't exist yet:

- **User Data Folder** — the per-user folder holding `user-settings.json`. Every time settings
  are saved, the previous file is rotated into `user-settings.json.bak.1` (shifting any older
  backups down, up to `.bak.5`) before the new one is written, so a bad edit or a crash mid-save
  always leaves a recent copy to restore from.
- **Machine Data Folder** — the shared, machine-wide folder used by the background service
  (`machine-settings.json`, index caches, and service-side logs).

The installed build uses `%LocalAppData%\Lertaro` and `%ProgramData%\Lertaro`. A portable build uses `Data\Users\<SID hash>` and `Data\Machine` beside the application; if that portable `Data` folder has not been created yet, existing installed data is reused for compatibility. See [Portable data](../getting-started#portable-data) for the precedence rule.

## Checking for updates

- **Check Update** button — queries for a newer release; the button's own label reflects progress
  ("Checking...", then either "up to date" or the new version number found).
- If a non-administrator account can't stop the background service to install an update in place,
  a warning banner explains this and points you to the manual download page instead.
- Once a new version is found:
  - **Silent Auto-Update** — downloads and installs in the background, showing a progress bar,
    then restarts Lertaro automatically.
  - **Go to Download Page** — opens the GitHub release page in your browser for a manual install.

This mirrors the **Auto check for updates** / **Auto silent update** checkboxes under
[General → System](./general#system) — those control whether this check happens automatically on
startup; this page lets you trigger it manually at any time.
