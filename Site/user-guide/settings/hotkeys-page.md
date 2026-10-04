# Hotkeys Settings

The Hotkeys settings page centralizes management of global summon hotkeys, in-app navigation keys, plugin action shortcuts, and foreground process filtering rules. The top tabs include: **Global**, **Plugin Actions**, and **Process Blacklist**.

## 1. Global

### Global Hotkeys Group

- **Show/Hide Quick Search**: Dedicated key recording box. Supports **double-tap mode** (default double `Ctrl`, configurable to double `Alt` or `Shift`) as well as **standard key combinations** (e.g. `Alt+Space`).
- **Open full panel by default**: Checkbox (default disabled). When enabled, the global summon hotkey opens the Full Window instead of the Quick Window. The first summon brings it to the foreground once; pressing the hotkey while it is visible but inactive refocuses it, and pressing it again when it is active returns to the Quick Window by default (or closes outright when **Close Full Window on Repeat Hotkey** is enabled). The window is not automatically kept topmost.
- **Respond when focused on full-screen applications**: Checkbox (default disabled). When enabled, Lertaro responds to hotkeys even when an exclusive fullscreen game or media player is active; when disabled, keys are bypassed silently to protect gameplay.
- **Quick Jump**: Default `Ctrl+G`. In file dialogs, jumps immediately to the directory most recently browsed in supported file managers.
- **Quick Navigation Menu**: No shortcut is assigned by default. You can assign an optional global shortcut to open the cascading menu. From the desktop or an ordinary app it uses desktop context; in File Explorer and native file dialogs it uses the current window context. File managers and file dialogs remain allowed even when ordinary foreground protections suppress global hotkeys.

### Navigation & Function Keys Group

Provides dedicated key recording controls accepting custom single keys or combinations:

- **Select Next / Previous Item**: Default `Ctrl+N` / `Ctrl+P` (equivalent to `↓` / `↑`).
- **Jump to Result Modifier**: Default `Ctrl`, used with numbers `1`–`9` for instant activation.
- **Open Action Menu**: Default `Ctrl+O` (equivalent to `→`). Both apply to the Quick Window and to the inline card outside file dialogs; the Full Window opens the same menu with right-click or the `Apps` key.
- **Autocomplete from Selection**: Default `Ctrl+Tab`.
- **QuickLook Instant Preview**: Default `Alt+P`.
- **Previous / Next Search Term**: Default `Alt+Up` / `Alt+Down`.
- **Delete Search History Term**: Default `Ctrl+Delete`.
- **Open Full Window**: Default `Ctrl+F`.
- **Open LocalSend Window**: Default `Ctrl+S`.
- **Pin Window (Keep Visible)**: Default `Ctrl+T`.
- **Toggle Quick Panel**: Default `Ctrl+F2`.

### Quick Navigation Mouse Triggers Group

- **Double-click left button on blank area**: Checkbox (**disabled by default**). Pops up the Quick Navigation menu on desktop or File Explorer empty spaces.
- **Middle-click on blank area**: Checkbox (default enabled). Pops up the Quick Navigation menu on desktop, File Explorer, or open/save file dialogs.

Only the desktop and File Explorer honor the double-click form; third-party file managers accept middle-click alone. Both triggers are skipped while the pointer is over Lertaro's own card, and both stay alive for file managers and dialogs even when a blacklist or fullscreen would normally silence global triggers.

### Recording a Hotkey

Every key press goes into the box instead of the app behind it, which makes the recorder itself a set of gestures:

- **`Escape`** cancels the recording and clears the value. There is no "discard and restore" — the box is empty the moment you press it.
- **`✕`** clears the box; **`↺`** restores that row's factory default. Exactly one of the two is shown at a time — clear while a combination is set, restore once it is empty.
- **A bare modifier** (`Ctrl`, `Alt`, `Shift`) is accepted only on rows that mean something by itself — the summon hotkey, which becomes a double-tap, and the jump modifier. On an ordinary combination row, pressing and releasing a lone modifier **clears** the row rather than saving a value that could never fire.
- **Windows-reserved combinations** (`Win+E`, `Win+D`, …) are refused: the value is coerced to empty, so the box simply goes blank.
- **No conflict detection.** Two rows can hold the same chord and nothing warns you; only favorite-item hotkeys report a duplicate or an OS-refused combination, and only after you apply.

### Per-Favorite Hotkeys

Every favorite ([**Settings → Favorites**](./favorites)) can carry its own **OS-level global** hotkey, which works even when Lertaro has no window open and jumps the foreground file manager to that folder. Unlike the recorder rows above it requires a modifier, refuses a lone modifier, and cannot use `F12`.

## 2. Plugin Actions

All action shortcuts registered by plugins (e.g. Copy Full Path `Ctrl+Shift+C`, Copy Name `Shift+C`, Cut `Ctrl+X`, Copy `Ctrl+C`, Paste `Ctrl+V`, Delete `Delete`, Permanent Delete `Shift+Delete`) are grouped here.

- **Categorized View**: Neatly organized by the originating plugin.
- **Rebindable**: Each action includes its own key recording control.

## 3. Process Blacklist

Configures silence rules for specific foreground applications. When a blacklisted process is focused, Lertaro bypasses all global hotkeys and mouse triggers without interception.

- **Case-Insensitive**: Both `game.exe` and `game` are matched.
- **Add Single Entry**: Type the process name and click **Add Process**.
- **Batch Editing**: Click **Generate Text** to export current entries to multi-line text, or paste a list and click **Apply to List** for batch updates.
- **File Dialog Exemption**: Even if an application is blacklisted, its native file selection dialogs remain exempted, so Inline Search and Quick Navigation keep working.
