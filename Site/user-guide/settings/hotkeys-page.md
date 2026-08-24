# Hotkeys Settings

The Hotkeys settings page centralizes management of global summon hotkeys, in-app navigation keys, plugin action shortcuts, and foreground process filtering rules. The top tabs include: **Global**, **Plugin Actions**, and **Process Blacklist**.

## 1. Global

### Global Hotkeys Group

- **Show/Hide Quick Search**: Dedicated key recording box. Supports **double-tap mode** (default double `Ctrl`, configurable to double `Alt` or `Shift`) as well as **standard key combinations** (e.g. `Alt+Space`, `Win+Space`).
- **Respond when focused on full-screen applications**: Checkbox (default disabled). When enabled, Lertaro responds to hotkeys even when an exclusive fullscreen game or media player is active; when disabled, keys are bypassed silently to protect gameplay.
- **Quick Jump**: Default `Ctrl+G`. In file dialogs, jumps immediately to the directory most recently browsed in supported file managers.

### Navigation & Function Keys Group

Provides dedicated key recording controls accepting custom single keys or combinations:

- **Select Next / Previous Item**: Default `Ctrl+N` / `Ctrl+P` (equivalent to `↓` / `↑`).
- **Jump to Result Modifier**: Default `Ctrl`, used with numbers `1`–`9` for instant activation.
- **Open Action Menu**: Default `Ctrl+O` (equivalent to `→`).
- **Autocomplete from Selection**: Default `Ctrl+Tab`.
- **QuickLook Instant Preview**: Default `Alt+P`.
- **Previous / Next Search Term**: Default `Alt+Up` / `Alt+Down`.
- **Delete Search History Term**: Default `Ctrl+Delete`.
- **Open Full Window**: Default `Ctrl+F`.
- **Open LocalSend Window**: Default `Ctrl+S`.
- **Pin Window (Keep Visible)**: Default `Ctrl+T`.
- **Toggle Quick Panel**: Default `Ctrl+F2`.

### Quick Navigation Mouse Triggers Group

- **Double-click left button on blank area**: Checkbox (default enabled). Pops up the Quick Navigation menu on desktop or File Explorer empty spaces.
- **Middle-click on blank area**: Checkbox (default enabled). Pops up the Quick Navigation menu on desktop, File Explorer, or open/save file dialogs.

## 2. Plugin Actions

All action shortcuts registered by plugins (e.g. Copy Full Path `Ctrl+Shift+C`, Cut `Ctrl+X`, Copy `Ctrl+C`, Paste `Ctrl+V`, Delete `Delete`, Permanent Delete `Shift+Delete`) are grouped here.

- **Categorized View**: Neatly organized by the originating plugin.
- **Rebindable**: Each action includes its own key recording control.

## 3. Process Blacklist

Configures silence rules for specific foreground applications. When a blacklisted process is focused, Lertaro bypasses all global hotkeys and mouse triggers without interception.

- **Case-Insensitive**: Both `game.exe` and `game` are matched.
- **Add Single Entry**: Type the process name and click **Add Process**.
- **Batch Editing**: Click **Generate Text** to export current entries to multi-line text, or paste a list and click **Apply to List** for batch updates.
- **File Dialog Exemption**: Even if an application is blacklisted, its native file selection dialogs remain exempted, ensuring seamless Inline Search and Quick Navigation.
