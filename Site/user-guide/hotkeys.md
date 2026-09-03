# Hotkeys & Gestures

Lertaro embraces a keyboard-first interaction philosophy while offering rich mouse gestures and quick cascading navigation. Except for non-configurable core keys, all global and in-app hotkeys can be customized under [**Settings → Hotkeys**](./settings/hotkeys-page).

## 1. Global Hotkeys Cheat Sheet

| Action | Default Hotkey | Description & Interaction Details |
| :--- | :--- | :--- |
| **Toggle Quick Window** | Double-tap `Ctrl` | Can be set to a double-tap mode or standard key combinations (e.g. `Alt+Space`, `Win+Space`). When **Open full panel by default** is enabled, this shortcut opens the Full Window instead: it is brought to the foreground once when first shown, refocused when visible but inactive, and closed when already active. It is not automatically kept topmost. |
| **Quick Jump** | `Ctrl+G` | Jumps file dialogs directly to the directory most recently browsed in a supported file manager or Quick Panel. |
| **Select Next Item** | `Ctrl+N` or `↓` | Moves highlight down. Navigates seamlessly across groups in the Quick Panel. In Quick Launch, the arrow keys follow the visible grid; **←** and **→** cross row boundaries, while **↑** and **↓** keep the current column. When Quick Launch is visible with an empty query, `Ctrl+N` cycles to the next data source and wraps at the end. |
| **Select Previous Item** | `Ctrl+P` or `↑` | Moves highlight up. Navigates seamlessly across groups in the Quick Panel. In Quick Launch, the arrow keys follow the visible grid; **←** and **→** cross row boundaries, while **↑** and **↓** keep the current column. When Quick Launch is visible with an empty query, `Ctrl+P` cycles to the previous data source and wraps at the beginning. |
| **Jump to Results 1–9** | `Ctrl` + `1`–`9` | Modifier is customizable. Number badges appear next to visible items for instant activation. |
| **Open Action Menu** | `Ctrl+O` or `→` | Expands the context action menu (copy path, properties, run as admin, file operations, etc.). |
| **Autocomplete from Selection** | `Ctrl+Tab` | Fills the search box with the selected item's name or full path for secondary refinement. |
| **QuickLook Instant Preview** | `Alt+P` | Opens or closes the side preview panel (images, documents, audio/video playback, folder trees). |
| **Previous Search Term** | `Alt+Up` | Steps backward through recent search query history. |
| **Next Search Term** | `Alt+Down` | Steps forward through recent search query history. |
| **Delete Search History Term** | `Ctrl+Delete` | Removes the currently displayed keyword from search history. |
| **Open Full Window** | `Ctrl+F` | Opens the full-sized main search window, carrying over the current query. |
| **Open LocalSend Window** | `Ctrl+S` | Opens the LocalSend wireless LAN transfer window to quickly send files or text to other devices. |
| **Pin Window (Keep Visible)** | `Ctrl+T` | Temporarily locks the window open when losing focus (ideal for pasting multi-part queries). |
| **Toggle Quick Panel** | `Ctrl+F2` | Docks the quick panel beside the current active window for recent files, favorites, and workspaces. |

## 2. Search Box Icon & Mouse Gestures

The small logo inside the search box is not just an indicator — it provides several quick mouse gestures:

### Quick Window Icon Gestures

- **Left-click**: Pops up the main context menu at the cursor (Show Full Window, Toggle Hotkey, Settings, About, Clean Exit, Exit). "Show Full Window" carries over your active query.
- **Left-click & Drag**: Drags the search bar to reposition it. **Holding `Ctrl` while dragging** locks movement strictly to the **vertical axis**, keeping horizontal alignment intact.
- **Right-click**: Instantly resets the Quick Window back to its default centered screen position without altering configured dimensions.
- **Middle-click**: Toggles the "Pin Window" state. The logo illuminates while pinned.

> [!NOTE]
> Coordinates remembered for the Quick Window are **proportional relative coordinates** on that specific display. When invoked on another monitor with different resolutions or DPI scalings, Lertaro scales position automatically without escaping visible bounds.

### Inline and Full Window Icons

- **Inline Window**: When embedded in native file dialogs (Open/Save/Browse), left-clicking the logo triggers the [**Quick Navigation**](#3-quick-navigation-mouse-triggers) cascading menu; disabled in ordinary Explorer windows.
- **Full Window**: Left-clicking the logo opens the context menu; **Open Full Window** is hidden there because the window is already open. Middle-clicking toggles the window's pinned state.

## 3. Quick Navigation (Mouse Triggers)

Quick Navigation lets you access frequently used directories and recent files with mouse clicks alone without typing.

### Triggering Environments

- **Desktop Blank Area**: Middle-click (or optional double-left-click) to open the menu. Clicking a folder or file opens it directly.
- **File Explorer**: Middle-click empty areas in File Explorer; clicking an item navigates the current window directly to that folder.
- **Third-party File Managers**: Middle-click file list areas in Directory Opus, Total Commander, XYplorer, Files, and One Commander (see [**Supported File Managers**](./file-manager-support)).
- **File Dialogs**: Middle-click or click the embedded logo inside Open/Save/Browse dialogs to jump instantly to the target folder without accidentally triggering confirmation.

### Cascading Menu Structure

Powered by the **Folder Cascader** plugin:

1. **Currently Open Folders**: Aggregates and deduplicates active folders from all open file managers.
2. **Favorites & History**: Lists starred folders, files, and recent visit histories.
3. **Custom Categories**: Configure nested submenus under **Settings → Plugins → Folder Cascader** (e.g. `Work/ProjectA`).
4. **Quick Add Folder (`+` Button)**: Every submenu header features a small `+` button to save the currently browsed directory directly into that category.

## 4. Hardcoded Core Keys (Non-configurable)

To ensure consistent and deterministic interaction, the following keys behave identically across all configurations:

| Key | Context | Standard Behavior |
| :--- | :--- | :--- |
| `Enter` | Result List | Opens the selected item (file, folder, app, or action). |
| `Ctrl+Enter` | Result List | Reveals and selects the item in Windows File Explorer. |
| `Ctrl+Shift+Enter` | Result List | Launches the selected item with administrative privileges. |
| `Escape` | Any Context | Clears the query if text exists; closes the window or exits the menu if already empty. |
| `Backspace` | Action Menu | Exits the action menu back to the search list when filter text is empty. |
| `←` / `→` Arrow Keys | Action Menu | Left arrow navigates back to parent menu; right arrow enters submenus. |
| `Alt+Space` | All Lertaro Windows | Suppressed to prevent triggering system titlebar menus on borderless windows. |
| `Alt+F4` | Full / Settings Windows | Closes window normally; suppressed on Quick, Inline, and Preview floating windows. |

## 5. Plugin Action Hotkeys & Process Blacklist

### Plugin Action Hotkeys

Plugins can register specific action shortcuts (e.g. `Ctrl+Shift+C` for copying paths, or file operations: Cut `Ctrl+X`, Copy `Ctrl+C`, Paste `Ctrl+V`, Delete `Delete`, Permanent Delete `Shift+Delete`). Manage and rebind them under **Settings → Hotkeys → Plugin Actions**.

### Process Blacklist & Fullscreen Bypass

- **Automatic Fullscreen Bypass**: When a focused foreground application runs in exclusive fullscreen mode (e.g. 3D games or video players), Lertaro automatically bypasses all global hotkeys to avoid interrupting gameplay.
- **Custom Process Blacklist**: Add executable names under [**Settings → Hotkeys**](./settings/hotkeys-page#process-blacklist) (e.g. `game.exe`) to silence hotkeys and mouse triggers while that process is focused.
