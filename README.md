<p align="center">
  <img src="App/logo.png" alt="Lertaro logo" width="120">
</p>

# ⚡ Lertaro

English | [简体中文](Readmes/zh-CN.md) | [繁體中文（香港）](Readmes/zh-HK.md) | [繁體中文（台灣）](Readmes/zh-TW.md) | [日本語](Readmes/ja-JP.md) | [한국어](Readmes/ko-KR.md) | [Español](Readmes/es-ES.md)

> [!CAUTION]
> **Security notice: download Lertaro only from official sources.** The repository `github.com/adelmagical742/Lertaro` and website `adelmagical742.github.io` impersonate Lertaro and distribute malicious downloads. Do not download or run any file from them. The only official repository is [Lertaro/Lertaro](https://github.com/Lertaro/Lertaro), the only official website is [lertaro.github.io](https://lertaro.github.io/), and official binaries are published only through [GitHub Releases](https://github.com/Lertaro/Lertaro/releases). Treat these impersonating sources as untrusted even if their filenames or content change.

Lertaro is an ultra-lightweight, high-performance, extensible global search and productivity launcher for Windows, built on **.NET 10 (WPF)**. It's a modern, open-source alternative to **Listary** and **Everything** — indexing local drives via the NTFS **USN Journal** and $MFT for near-instant, low-resource search.

📖 **[Full Documentation, User Manual & Developer Manual](https://lertaro.github.io/)**

## Highlights

- ⚡ **USN & MFT Low-Level Indexing** — reads the NTFS/ReFS USN Change Journal and $MFT directly instead of walking directories; a lightweight background service keeps the index in sync in real time with FAT32/exFAT monitoring and network share caching.
- 🎯 **fzf-Style Fuzzy Search & Aliases** — multi-keyword jump matching with directory path tokens and prefix/suffix/exact/exclude operators, plus non-ASCII pinyin alias transliteration.
- 📂 **Three Window Modes & Deep Docking** — a quick popup window, a full main window, and an inline bar that automatically docks into native Open/Save file dialogs and major file managers (File Explorer, Total Commander, Directory Opus, OneCommander).
- 🎬 **Actions Menu & QuickLook Preview** — press `Ctrl+O` for the actions menu with native Shell right-click integration, or press `Alt+P` for seamless QuickLook file previews.
- 📊 **Instant Disk Space Treemap** — explore indexed disk usage visually in a real-time Treemap without rescanning disks, drill down into oversized folders, and execute cleanup actions.
- 🧩 **Open Plugin SDK & Ecosystem Bridge** — clean .NET 10 C# SDK contracts for custom search providers, aliases, actions, columns, and previews, plus compatibility with Flow Launcher community plugins.
- 🛡️ **3-Process Isolation & Offline Privacy** — SYSTEM background service (`Lertaro.Service`), user-mode WPF UI (`Lertaro.App`), and UIPI-bypassing hook helper (`Lertaro.Service --hook`) are strictly isolated. 100% local with zero telemetry.

See the **[User Manual](https://lertaro.github.io/user-guide/)** for search syntax, every hotkey, and every settings option; the **[Developer Manual](https://lertaro.github.io/dev-guide/)** for architecture and the plugin SDK reference.

## Download

Grab the latest release from the [homepage](https://lertaro.github.io/) or directly:

- **x64 (Intel / AMD)**
  - [Installer (Lertaro-Setup.exe)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup.exe) — recommended, supports the background service.
  - [Portable (Lertaro-Portable.zip)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable.zip) — no install, unzip and run.
- **ARM64 (Native for Snapdragon / Windows on ARM)**
  - [Installer (Lertaro-Setup-arm64.exe)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Setup-arm64.exe) — recommended for ARM devices.
  - [Portable (Lertaro-Portable-arm64.zip)](https://github.com/Lertaro/Lertaro/releases/latest/download/Lertaro-Portable-arm64.zip) — native portable build for ARM.

## Building from Source

Requirements: Windows 10/11, .NET 10 SDK, Visual Studio 2022 or JetBrains Rider, and [Inno Setup](https://jrsoftware.org/isinfo.php) if you want to build the installer.

- `build_and_run.bat` — rebuilds App/Core/Service/plugins and relaunches everything locally.
- `make.bat` — produces Release builds for both x64 and ARM64 in `dist/`.

See the **[Developer Manual](https://lertaro.github.io/dev-guide/)** for the full architecture and plugin SDK.

## 🎁 Support & Donation

If Lertaro has been useful to you, thank you for considering a donation!

- **USDT (TRC20)**: `TNDh3husX1trDW2ZPm4ZZYdoCoCRCZQXn5`

## License

MIT License.
