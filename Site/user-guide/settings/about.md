# About & Updates

The About page displays component versions, provides one-click access to user and system storage directories, and supports manual update checks and seamless in-place upgrades. The page is located at **Settings → About**.

## 1. Component Versions & Project Info

The top section displays independent version numbers across Lertaro's four architectural components:

- **App Version**: Foreground WPF user interface version.
- **Core Version**: Core search and indexing logic library version.
- **Service Version**: Background Windows indexing service version (text color dynamically indicates service connectivity and health).
- **CLI Version**: Command-line interactive utility `lff` version.

Direct links to the official website, GitHub repository, and documentation center are provided below.

## 2. Data Directories & 5-Stage Backup Rotation

Clickable links directly open storage directories in File Explorer (folders are created automatically if they do not yet exist):

### User Data Directory

- **Contents**: Stores per-user settings (`user-settings.json`), search and keyword history, user caches, and security certificates.
- **5-Stage Backup Rotation**: Whenever settings are saved, Lertaro automatically rotates existing configuration files to `user-settings.json.bak.1`, cascading up to `.bak.5`. Even in unexpected power cuts or misconfigurations, you can restore from the latest 5 backups.

### Machine Data Directory

- **Contents**: Stores machine-level configuration (`machine-settings.json`), persistent physical index caches, and background service logs.

### Storage Path Architecture

- **Installer Version**: User data is placed in `%LocalAppData%\Lertaro`, and machine data in `%ProgramData%\Lertaro`.
- **Portable Version**: User data is placed in `Data\Users\<SID hash>`, and machine data in `Data\Machine` next to the executable (see [**Portable Data Isolation**](../getting-started#portable-data-isolation-architecture)).

## 3. Update Checks & In-Place Upgrades

- **Check for Updates**: Manually queries online repositories for newer releases with dynamic button state feedback ("Checking for updates..." → "Up to date" or release version details).
- **Upgrade Paths When Updates Are Found**:
  - **Silent In-Place Update** — Downloads and installs updates in the background, restarting Lertaro seamlessly.
  - **Go to Download Page** — Opens the GitHub Releases page in your default browser for manual package downloads.
- **Permission Safety Notices**: If running under a non-administrator account unable to restart the background service for in-place updates, a clear guidance banner directs you to the manual download page.
