# Settings Reference

Lertaro provides a comprehensive and granular suite of customization options. Whether you want to fine-tune search bar pixel dimensions, customize global hotkeys, adjust drive indexing schedules, or manage third-party plugins and workspaces, everything can be configured in the Settings Center.

## 1. Window Features & Navigation

- **Resizable & Maximize Support**: The Settings window supports smooth border resizing, titlebar double-click maximizing, and persistent size memory across sessions.
- **Global Settings Search**: A search bar sits in the upper right corner of the Settings titlebar. Powered by Lertaro's fzf fuzzy matching engine, it searches across all sections (including plugin settings and actions). Selecting a result jumps straight to that setting item and highlights it with a temporary flashing border.
- **Scrollable Tab Bars**: In sections containing multiple nested sub-tabs (such as General, Hotkeys, Indexing), overflow navigation arrows appear on either side to ensure all tabs remain accessible across all UI languages.

## 2. Settings Sections Overview

The sidebar contains the following core sections:

| Section | Core Capabilities |
| :--- | :--- |
| **[Service Status](./service-status)** | Windows background service state, health diagnostics, and live log viewers for Service / App / Hook processes. |
| **[Indexing Management](./index-drives)** | Local drives (NTFS / ReFS / FAT32), network shares (SMB / NAS), WSL distributions, custom folders, and three exclusion rule types. |
| **[General Settings](./general)** | Autostart, hardware acceleration, IPC compatibility, search bar dimensions, Full Window column ordering, and preview parameters. |
| **[Hotkeys](./hotkeys-page)** | Global search hotkeys, navigation keys, plugin action shortcuts, process blacklist, and fullscreen bypass rules. |
| **[Plugins](./plugins)** | Installed plugins list, component toggles, custom configuration forms, and Flow Launcher community bridge. |
| **[LocalSend](./localsend)** | Wireless local network transfer protocol configuration (device name, port, PIN code, and auto-save options). |
| **[Favorites](./favorites)** | Starred folders, files, and URLs with drag-and-drop ordering and quick alias recall. |
| **[History](./history)** | Search result recall history, keyword query history, and cleanup tools. |
| **[Quick Panel](./quick-panel)** | Floating docked workspaces, folder source aggregations, drag-and-drop file ingestion, and plugin dynamic tabs. |
| **[Appearance & Themes](./appearance)** | Light / Dark / Follow System mode toggle and real-time visual thumbnail cards for built-in and curated themes. |
| **[About & Updates](./about)** | Component version breakdown, user/machine data directory paths, backup rotation mechanisms, and in-place silent updates. |

The following chapters provide a complete walkthrough of all settings, parameter ranges, and default behaviors for each section.
