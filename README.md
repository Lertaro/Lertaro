<p align="center">
  <img src="logo.svg" alt="Lertaro logo" width="120">
</p>

# Lertaro

English | [简体中文](Readmes/zh-CN.md) | [繁體中文（香港）](Readmes/zh-HK.md) | [繁體中文（台灣）](Readmes/zh-TW.md) | [日本語](Readmes/ja-JP.md) | [한국어](Readmes/ko-KR.md) | [Español](Readmes/es-ES.md)

> [!CAUTION]
> **Security notice: download Lertaro only from official sources.** For your security, download Lertaro only from the official repository, website, and [GitHub Releases](https://github.com/Lertaro/Lertaro/releases). Do not download or run files from unverified sources or repositories.

Lertaro is an open-source file search and productivity launcher for Windows, built with .NET 10 / WPF. A background index helps you find files, folders, and applications, with search, previews, and folder navigation built into your daily workflow.

[User manual](https://lertaro.github.io/user-guide/) · [Developer manual](https://lertaro.github.io/dev-guide/) · [Releases](https://github.com/Lertaro/Lertaro/releases) · [Report an issue](https://github.com/Lertaro/Lertaro/issues)

## Features

- **File search**: builds NTFS indexes from the MFT and tracks NTFS / ReFS changes through the USN Journal; also supports FAT32 / exFAT and network shares. Includes fuzzy matching, multiple keywords, path and type filters, and full pinyin or initials for Chinese names.
- **Document content search**: the content search plugin indexes text, Markdown, PDF, and Office documents and displays matching snippets. Configure watched folders, then search with `cs keywords`.
- **Windows and folder navigation**: quick, full, and inline search windows integrate with Windows Open/Save dialogs, File Explorer, and file managers such as Total Commander and Directory Opus, with Quick Jump and navigation menus.
- **File actions and previews**: copy, move, rename, delete, and access native context menus; preview images, text, media, and folders, with an optional bridge to the standalone QuickLook application.
- **Favorites and workspaces**: Quick Launch brings together pinned items, favorites, and history. Quick Panel organizes folders, recent files, and drag-and-drop actions into workspaces that can follow the foreground application.
- **Disk space analysis**: explore a treemap of indexed disk usage and drill down into large files and folders.
- **Productivity plugins**: calculator, web search, translation, browser bookmarks and history, process management, window switching, custom commands, and more. Extend the app with the C# SDK or the Flow Launcher plugin bridge.
- **Terminal and external integration**: `lff` offers interactive selection, scripted queries, and JSON output. Open searches and settings through `lertaro://`, or enable the Everything IPC compatibility service.
- **LAN transfers and customization**: send files, folders, and text through the built-in LocalSend protocol implementation. Customize themes, language, and shortcuts, and back up or restore settings.

Local file indexing and search work offline. Web search, translation, and update checks use the internet as needed; LocalSend uses your local network. Indexing, the desktop UI, and window integration run in separate processes.

## Download and install

Supports **Windows 10 / 11 on x64 and ARM64**. The app requires the matching **.NET 10 Desktop Runtime**. The installer detects and installs it when needed; portable users can run the included `install-dotnet-runtime.bat`.

Download official packages from this repository's [GitHub Releases](https://github.com/Lertaro/Lertaro/releases):

| Architecture | Installer (recommended) | Portable |
| --- | --- | --- |
| x64 (Intel / AMD) | [Lertaro-Setup.exe](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup.exe) | [Lertaro-Portable.zip](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable.zip) |
| ARM64 (Windows on ARM) | [Lertaro-Setup-arm64.exe](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup-arm64.exe) | [Lertaro-Portable-arm64.zip](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable-arm64.zip) |

Run the installer, or fully extract the portable ZIP and launch `Lertaro.App.exe`. The first launch requests administrator approval to install the indexing service. Once the service is installed, updating or deleting a portable installation also requires administrator rights; run `portable-cleanup.bat` to remove the service before deleting the folder. See [Getting started](https://lertaro.github.io/user-guide/getting-started).

### Using Scoop

With [Scoop](https://scoop.sh/) installed, add the official bucket in a regular PowerShell terminal:

```powershell
scoop bucket add lertaro https://github.com/Lertaro/Lertaro
```

Then install from an **administrator PowerShell terminal under the same user account**:

```powershell
scoop install --global lertaro/lertaro-np
```

To upgrade, refresh the bucket and update the package from an administrator terminal:

```powershell
scoop update
scoop update --global lertaro-np
```

To uninstall, use an administrator terminal:

```powershell
scoop uninstall --global lertaro-np
```

`lertaro-np` runs the official installer, using the existing registered location or Program Files by default. The Scoop directory stores the installer and package records. Upgrades retain the installation directory, and uninstalling preserves user settings. Launch Lertaro from the Start menu; open a new terminal to use `lff`.

Scoop, manual installation, and WinGet share one installation, so choose one channel to maintain it. The in-app updater does not update Scoop's version records. See the [package manager guide](dev-docs/package-managers.md) (Chinese) for WinGet publication status, reinstalling the same version, and installation logs.

## Getting started

1. Launch `Lertaro.App.exe` and complete service installation. Check initial indexing progress in **Settings → Running Status**, and choose drives or add folders in the index settings.
2. Double-tap the same `Ctrl` key to open Quick Search. Type a file or application name, or pinyin; select with `↑` / `↓`, press `Enter` to open, or `Ctrl+Enter` to reveal its location.
3. Enable and configure extensions in **Settings → Plugins**. Content search requires watched folders to be configured first.

| Default shortcut | Action and context |
| --- | --- |
| Double-tap `Ctrl` | Show/hide Quick Search |
| `Ctrl+F` | Open the full search window from Quick Search |
| `Ctrl+O` | Open the actions menu in Quick Search; use right-click in the full window |
| `Alt+P` | Toggle the preview panel in windows that support previews |
| `Ctrl+G` | Jump to the recently browsed folder from a file dialog |
| `Ctrl+F2` | Open Quick Panel |

`lff` queries require Lertaro App to be running. Select the installer's PATH option (Scoop selects it automatically), then open a new terminal:

```powershell
lff --search 'report' --limit 20
```

Learn more: [Search syntax](https://lertaro.github.io/user-guide/search-syntax) · [Shortcuts](https://lertaro.github.io/user-guide/hotkeys) · [CLI](https://lertaro.github.io/user-guide/cli) · [Troubleshooting](https://lertaro.github.io/user-guide/troubleshooting)

## Build from source

Install the .NET 10 SDK on Windows, then get the source and run:

```powershell
git clone https://github.com/Lertaro/Lertaro.git
cd Lertaro
.\build_and_run.bat
```

This script stops the running Lertaro app and service, builds App, Service, CLI, and plugins, then launches the development build.

Release packaging uses `make.bat` and additionally requires PowerShell 7, 64-bit Inno Setup 7, and a `LERTARO_SIGNING_KEY` matching the updater's public key. It produces installers, portable ZIPs, and signatures for both architectures in `dist/`. See the [release guide](dev-docs/gitlab-self-hosted-runner.md) (Chinese) for configuration and the [developer manual](https://lertaro.github.io/dev-guide/) for plugin development.

## Feedback and contributions

[Issues](https://github.com/Lertaro/Lertaro/issues) and pull requests are welcome. Include the app version, Windows version and architecture, installation method, reproduction steps, and relevant logs when reporting a bug. See the [maintainer documentation](dev-docs/README.md) (Chinese) before contributing.

## License

[MIT License](LICENSE).
