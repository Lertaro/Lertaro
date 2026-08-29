# Getting Started

Welcome to Lertaro! Lertaro is an ultra-fast file search launcher and productivity tool purpose-built for Windows. This guide walks you through installation options, core architecture, three distinct window modes, and essential search workflows.

## 1. Download & Installation

You can get the latest release from the official homepage. Each release provides installer and portable artifacts for native **x64** and **ARM64** application builds:

### Installer (`Lertaro-Setup.exe`, Recommended)

- **Automated Configuration**: The setup wizard automatically registers the background indexing service (`Lertaro.Service`), configures startup entries, and installs required .NET desktop runtime components.
- **Seamless Upgrades**: Supports background update checks and one-click in-place upgrades.

### Portable Edition (`Lertaro-Portable.zip`)

- **Extract and Run**: Unzip to any folder and run immediately without installation.
- **Runtime Dependency**: If your system lacks the required .NET desktop runtime, run the bundled `install-dotnet-runtime.bat` script once.
- **Self-Contained Data Storage**: The portable edition saves machine-wide data to `Data\Machine` alongside the application, and user settings to `Data\Users\<SID hash>`. If the `Data` directory does not exist yet, it falls back to `%ProgramData%\Lertaro` and `%LocalAppData%\Lertaro` for compatibility; once created, it prioritizes local data as a fully self-contained instance.
- **Clean Removal**: Before deleting the portable folder, run the bundled `portable-cleanup.bat` script. It stops and uninstalls the background service, and removes current-user `lertaro://` URI registrations and startup entries.

> [!TIP]
> If you are using a Windows on ARM device (such as Surface Pro X or Snapdragon laptops), download the native `Lertaro-Setup-arm64.exe` or `Lertaro-Portable-arm64.zip` for maximum performance and battery efficiency.

## 2. Architecture Overview

When running Lertaro for the first time, it installs and launches a dedicated Windows service (`Lertaro.Service`). Understanding this separation helps you get the most out of the system:

- **Foreground App (UI & Interaction)**: Renders search windows, floating panels, action menus, keyboard hooks, and interactive previews. The foreground process maintains a minimal memory footprint and instant responsiveness.
- **Background Service (Indexing & Data)**: Runs with service privileges in the background, continuously monitoring NTFS / ReFS USN change journals, tracking filesystem events, managing network drives, and maintaining an in-memory index tree.
- **Architectural Benefits**: Restarting, updating, or closing the UI never loses the background index or triggers full rescans. Heavy indexing tasks never stutter your keystrokes. You can check service status and health at any time under [**Settings → Service Status**](./settings/service-status).

## 3. Three Window Modes

Lertaro is not limited to a single search window. It adapts to different workflows with three purpose-built window modes:

| Window Mode | Default Trigger | Key Features & Design Focus | Best Used For |
| :--- | :--- | :--- | :--- |
| **Quick Window** | Double-tap `Ctrl` (Customizable) | Compact centered floating bar, optimized for muscle memory, number key jumps, and pure keyboard navigation | Frequent app launching, quick calculations, translations, and fast file lookup |
| **Full Window** | Taskbar/Start shortcut, or `Ctrl+F` | Full-featured large window with tabular results, sidebar filter groups, column sorting, and built-in Space Analyzer | Deep file browsing, broad exploration, disk space cleaning, and batch management |
| **Inline Window** | Automatically docks in file dialogs or Explorer | Embedded seamlessly into standard Windows file dialogs or third-party file managers | Quick destination locating when opening or saving files in external software |

All three window modes share the exact same underlying search engine, shortcut scheme, filter rules, and action menus.

## 4. First Search & Basic Navigation

### Type to Search

Simply open the search window and start typing. Results appear in real time (sub-millisecond latency). Search matching uses fuzzy jump matching by default — characters do not need to be contiguous. For advanced syntax, operators, and modifiers, see [**Search Syntax**](./search-syntax).

### Navigating & Opening Results

- **Move Selection**: Use arrow keys `↑` / `↓` (or configured navigation hotkeys `Ctrl+P` / `Ctrl+N`) to move selection up and down.
- **Open Directly**: Press `Enter` to open the highlighted file or launch the application.
- **Reveal in Explorer**: Press `Ctrl+Enter` to locate and select the item directly in Windows File Explorer.
- **Run as Administrator**: Press `Ctrl+Shift+Enter` to launch the selected application with administrative privileges.
- **Direct Number Jump**: In the Quick Window, press `Ctrl` + `1`–`9` to jump directly to any of the first 9 results.

### Action Menu & Context Actions

Press `Ctrl+O` or `→` on any highlighted item to expand the comprehensive **Action Menu**, offering path copying, file operations, properties, and plugin extensions. Read [**Actions & Preview**](./actions-and-preview) and [**Hotkeys**](./hotkeys) for more tips.
