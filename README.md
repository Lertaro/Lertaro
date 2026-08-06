<p align="center">
  <img src="App/logo.png" alt="Lertaro logo" width="120">
</p>

# ⚡ Lertaro

English | [简体中文](Readmes/zh-CN.md) | [繁體中文（香港）](Readmes/zh-HK.md) | [繁體中文（台灣）](Readmes/zh-TW.md) | [日本語](Readmes/ja-JP.md) | [한국어](Readmes/ko-KR.md) | [Español](Readmes/es-ES.md)

Lertaro is an ultra-lightweight, high-performance, extensible global search and productivity launcher for Windows, built on **.NET 10 (WPF)**. It's a modern, open-source alternative to **Everything** and **Listary** — indexing local drives via the NTFS **USN Journal** and MFT for near-instant, low-resource search.

📖 **[Full documentation, User Manual & Developer Manual](https://lertaro.github.io/)**

## Highlights

- **Millisecond indexing** — reads the NTFS USN Journal/MFT directly instead of walking directories; a low-footprint background service keeps the index in sync in real time.
- **FZF-style fuzzy search** — multi-keyword fuzzy matching with prefix/suffix/exact/exclude operators, plus pinyin aliasing for Chinese filenames.
- **Three ways to search** — a quick popup window, a full main window, and an inline bar that docks directly into File Explorer or native file dialogs.
- **QuickLook preview**, a right-click-style actions menu, and hotkeys that are all rebindable.
- **Open plugin SDK** — extend search providers, aliases, context-menu actions, result columns, previews, and themes.
- **Process isolation** — a SYSTEM-level indexing service kept separate from the per-user app UI.

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
