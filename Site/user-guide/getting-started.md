# Getting Started

## Installing

Grab the latest release from the [download button](../) on the homepage — two flavors are
published for every release:

- **Installer** (`Lertaro-Setup.exe`) — recommended. It registers the background indexing
  service and can start Lertaro with Windows.
- **Portable** (`Lertaro-Portable.zip`) — unzip and run, no installation. You can still install
  the background service later from **Settings → Service Status**. If your machine doesn't already
  have the .NET Desktop Runtime Lertaro needs, run the included `install-dotnet-runtime.bat` once
  — the Installer handles this step automatically, but the portable build can't. When you're done
  with a portable install, there's no uninstaller to clean up after you: double-click the included
  `portable-cleanup-registry.reg` (confirm the prompt) before deleting the folder to remove the two
  per-user registry entries Lertaro creates on its own — the `lertaro://` URI protocol
  registration and its "start with Windows" entry. Both are per-user (HKCU) only, so no
  administrator prompt is needed.

Each of those is published for **x64** and for **ARM64**. The names above are the x64 builds, which
run on any recent PC — including Windows on ARM, where they run emulated. On an ARM machine prefer
`Lertaro-Setup-arm64.exe` or `Lertaro-Portable-arm64.zip`, which are native. Automatic updates
stay on the architecture you installed, so moving between them means downloading the other build
yourself.

## Portable data

A portable copy stores machine data in `Data\Machine` and each user's private data in `Data\Users\<SID hash>` beside the application. If its `Data` folder has not been created yet, existing `%ProgramData%\Lertaro` and `%LocalAppData%\Lertaro` data is reused for compatibility; create `Data` when you want the portable copy to take precedence and be self-contained.

On first run, Lertaro installs and starts a Windows service (`Lertaro.Service`) that owns file
indexing. This split exists on purpose — see [Architecture](../dev-guide/architecture) if you're
curious why — but as a user, the only thing you need to know is: **Settings → Service Status**
tells you whether the service is installed and running, and lets you install it if it isn't.

## The three windows

Lertaro doesn't have just one search window — it adapts to how you're using it:

- **Main window** — the full window you get from the taskbar/Start Menu shortcut, with the largest
  result list and an in-window Actions panel.
- **Quick window** — the compact, always-on-top popup you summon with the global toggle hotkey
  (double-tap `Ctrl` by default). Built for "hit hotkey → type → Enter" muscle memory.
- **Inline window** — embeds a Lertaro search bar directly into a supported native file dialog or
  File Explorer window, so you can search without leaving the dialog you're already in.

All three share the same search engine, hotkey system, and Actions menu — the difference is purely
where and how they appear.

## Basic search

Recent files, favorites and history are reachable without typing anything at all — they are tabs in
the [Quick Panel](./settings/quick-panel), which opens over whatever window is in front rather than
inside the search window.

Just start typing. Results update as you type, ranked by relevance (see
[Search Syntax](./search-syntax) for how matching and ranking work). Use the
[configurable next/previous-item hotkeys](./hotkeys) (arrow keys by default) to move the
selection, and Enter to open the highlighted result.

Next up: [Search Syntax](./search-syntax) to get the most out of the query box.
