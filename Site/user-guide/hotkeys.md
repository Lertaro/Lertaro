# Hotkeys & Gestures

Lertaro embraces a keyboard-first interaction philosophy while offering rich mouse gestures and quick cascading navigation. Except for non-configurable core keys, all global and in-app hotkeys can be customized under [**Settings → Hotkeys**](./settings/hotkeys-page).

## 1. Global Hotkeys Cheat Sheet

| Action | Default Hotkey | Description & Interaction Details |
| :--- | :--- | :--- |
| **Toggle Quick Window** | Double-tap `Ctrl` | Can be set to a double-tap mode or standard key combinations (e.g. `Alt+Space`). A double-tap is two presses of the **same** modifier key released **100–300 ms** apart — left and right `Ctrl` are different keys, and any other key pressed in between restarts the count. In the double-tap form the modifier itself is never swallowed, so ordinary `Ctrl`+`C` style shortcuts keep working. When **Open full panel by default** is enabled, this shortcut opens the Full Window instead: it is brought to the foreground once when first shown, refocused when visible but inactive, and when already active a repeat press returns to the Quick Window by default (or closes outright when **Close Full Window on Repeat Hotkey** is enabled). It is not automatically kept topmost. |
| **Quick Jump** | `Ctrl+G` | Jumps file dialogs directly to the directory most recently browsed in Explorer, a supported file manager, or a file dialog. |
| **Quick Navigation Menu** | No default | An optional global shortcut opens the cascading Quick Navigation menu. From the desktop or an ordinary app it uses desktop context; in File Explorer and native file dialogs it uses the active window context. File managers and file dialogs remain allowed even when ordinary foreground protections suppress global hotkeys. |
| **Select Next Item** | `Ctrl+N` or `↓` | Moves highlight down. Navigates across groups in the Quick Panel. In Quick Launch, the arrow keys follow the visible grid; **←** and **→** follow the reading order across row boundaries, while **↑** and **↓** keep the current column and only move between adjacent rows, stopping where the target row has no item in that column. When Quick Launch is visible with an empty query, `Ctrl+N` cycles to the next data source and wraps at the end. |
| **Select Previous Item** | `Ctrl+P` or `↑` | Moves highlight up. Navigates across groups in the Quick Panel. In Quick Launch, the arrow keys follow the visible grid; **←** and **→** follow the reading order across row boundaries, while **↑** and **↓** keep the current column and only move between adjacent rows, stopping where the target row has no item in that column. When Quick Launch is visible with an empty query, `Ctrl+P` cycles to the previous data source and wraps at the beginning. |
| **Jump to Results 1–9** | `Ctrl` + `1`–`9` | Modifier is customizable. Number badges appear next to visible items for instant activation. The number means the Nth selectable row **from the first visible row**, so the badges renumber as you scroll; `0` is not bound. The Quick Launch panel is the exception — it binds `1`–`9`, `0` and `A`–`Z` for up to 36 tiles. |
| **Open Action Menu** | `Ctrl+O` or `→` | Expands the context action menu (copy path, properties, run as admin, file operations, etc.). Works in the Quick Window and in the inline card **outside** file dialogs; the Full Window opens the same menu with right-click or the `Apps` key (`Shift+F10`) instead. |
| **Autocomplete from Selection** | `Ctrl+Tab` | Fills the search box with the selected item's name or full path for secondary refinement. |
| **QuickLook Instant Preview** | `Alt+P` | Opens or closes the side preview panel (images, documents, audio/video playback, folder trees). Available in the Quick and Full Windows and the Quick Panel, not in the inline card; middle-clicking a previewable row toggles the same panel. |
| **Previous Search Term** | `Alt+Up` | Steps backward through recent search query history. |
| **Next Search Term** | `Alt+Down` | Steps forward through recent search query history. |
| **Delete Search History Term** | `Ctrl+Delete` | Removes the currently displayed keyword from search history. |
| **Open Full Window** | `Ctrl+F` | Opens the full-sized main search window, carrying over the current query. |
| **Open LocalSend Window** | `Ctrl+S` | Opens the LocalSend wireless LAN transfer window to quickly send files or text to other devices. |
| **Pin Window (Keep Visible)** | `Ctrl+T` | Temporarily locks the window open when losing focus (ideal for pasting multi-part queries). |
| **Toggle Quick Panel** | `Ctrl+F2` | Docks the quick panel beside the current active window for recent files, favorites, and workspaces. |

### Empty Inline Search in File Dialogs

When an inline search box is embedded in a native file dialog and the query is empty, the list shows the **Previous Directory** group first. If **Show Currently Open Folders in Inline Search** is enabled, it also shows a **Currently Open Folders** group collected from supported file managers, deliberately including the folder the dialog itself is in; duplicate paths are removed, empty groups stay hidden, and group headers do not receive shortcut badges. The setting is enabled by default and can be changed under [**Settings → General → System**](./settings/general). This behavior applies only to inline search in file dialogs; Quick and Full Windows are unchanged.

### Summoning and Focus Handover in the Inline Window

A card docked in a native file dialog deliberately **does not steal the keyboard**: the dialog keeps focus, so you keep typing into its own file-name field while the card mirrors the query. The gestures that move the caret are:

| Gesture | Effect | Conditions |
| :--- | :--- | :--- |
| **Type a letter or digit** | Summons the card and carries that first character into the search box; further keystrokes keep flowing in | Real keystrokes only — synthesized input from automation tools is passed through untouched, and nothing is captured while a context or system menu is open |
| **Double-tap `Ctrl`** (or your configured summon hotkey) | Puts the caret in the card's own search box. When the box already has the caret and the query is empty, it instead clears the query and hands focus back to the dialog's field, keeping the card open | The same hotkey as everywhere else, so rebinding it under [**Settings → Hotkeys**](./settings/hotkeys-page) moves this too. A file dialog stays eligible even when a blacklist or fullscreen would normally silence global hotkeys |
| **`Escape`** | While the **dialog** holds the keyboard, the card closes. While the **card** holds it, the query is cleared and focus returns to the dialog (in a file dialog) or the card closes (docked over a plain Explorer window) | |
| **`Backspace`** on an empty box | Leaves the search exactly like `Escape` | Only when the card has the caret; while the dialog holds the keyboard the key only edits the card's query |
| **`Enter`** on an empty box | Leaves the search like `Escape` | Only when the card has the caret — while the dialog holds the keyboard, `Enter` opens the highlighted browsing row |
| **`Tab`** | Always stays the dialog's own control traversal, so reach the card with the summon hotkey instead | |

> [!NOTE]
> Inside a file dialog the action menu is unavailable for the card (`Ctrl+O`, `→` and right-click do nothing there), and `Alt+P` preview is not offered either. Mouse still works on the rows: **left-click** opens the highlighted result, **`Ctrl`+click** opens it as administrator, a row can be **dragged out** into the dialog, and simply **hovering** moves the selection — which the host file manager mirrors.

## 2. Search Box Icon & Mouse Gestures

Besides branding, the small logo inside the search box also provides several quick mouse gestures:

### Quick Window Icon Gestures

- **Left-click**: Pops up the main context menu at the cursor (the same menu as the tray icon: Show Full Window, Send to other devices, Disable/Enable Hotkeys, Settings, About, Clean Exit, Exit). "Show Full Window" carries over your active query.
- **Left-click & Drag**: Drags the search bar to reposition it. **Holding `Ctrl` while dragging** locks movement strictly to the **vertical axis**, keeping horizontal alignment intact.
- **Right-click**: Instantly resets the Quick Window back to its default centered screen position without altering configured dimensions.
- **Middle-click**: Toggles the "Pin Window" state. The logo illuminates while pinned.

> [!NOTE]
> Coordinates remembered for the Quick Window are **proportional relative coordinates** on that specific display. When invoked on another monitor with different resolutions or DPI scalings, Lertaro scales position automatically without escaping visible bounds.

### Inline and Full Window Icons

- **Inline Window**: When embedded in native file dialogs (Open/Save/Browse), left-clicking the logo triggers the [**Quick Navigation**](#_3-quick-navigation-mouse-triggers) cascading menu; disabled in ordinary Explorer windows. Dragging the logo moves the card itself, and the offset is re-applied on every later dock.
- **Full Window**: Left-clicking the logo opens the context menu; **Open Full Window** is hidden there because the window is already open. Middle-clicking toggles the window's pinned state.

### Search Box Gestures (Quick Window)

- **Mouse wheel over the box**: Steps backward/forward through recent search terms — the same navigation as `Alt+Up` / `Alt+Down`, without lifting your hand from the mouse.
- **Middle-click on the box**: Deletes the history entry currently shown. This gesture is hardcoded and is not the configurable `Ctrl+Delete` binding.
- **Typing at any time** ends the history session, so the next wheel notch starts again from your current query. History does not wrap: the oldest term stops there, and the newest returns the query you started from.

### System Tray Icon

- **Left-click**: Toggles the Quick Window, exactly like the summon hotkey.
- **Right-click** (or a second click, since no double-click handler exists): Opens the menu at the cursor — Show Main Window, Send to other devices (only while LocalSend is enabled), Disable/Enable Hotkeys, Settings, About, Clean Exit (only when Lertaro is the only running process) and Exit.
- **Hide System Tray Icon** ([**Settings → General → System**](./settings/general)) removes the icon but never the in-window logo menu. While hotkeys are disabled from that menu, the icon is forced back on so you cannot lock yourself out.

## 3. Quick Navigation (Mouse Triggers)

Quick Navigation lets you access frequently used directories and recent files with mouse clicks alone without typing.

You can also assign an optional global keyboard shortcut under [**Settings → Hotkeys**](./settings/hotkeys-page). From the desktop or an ordinary app it opens the menu in desktop context; in File Explorer and native file dialogs it uses the active window context.

### Triggering Environments

- **Desktop Blank Area**: Middle-click (or optional double-left-click) to open the menu. Clicking a folder or file opens it directly.
- **File Explorer**: Middle-click empty areas in File Explorer; clicking an item navigates the current window directly to that folder.
- **Third-party File Managers**: Middle-click file list areas in Directory Opus, Total Commander, XYplorer, Files, and One Commander (see [**Supported File Managers**](./file-manager-support)).
- **File Dialogs**: Middle-click or click the embedded logo inside Open/Save/Browse dialogs to jump to the target folder without accidentally triggering confirmation.

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
| `Enter` | Result List | Opens the selected item (file, folder, app, or action). In the Full Window it opens **every** selected row. On an empty inline search box it leaves the search instead — there is no top match to open. |
| `Ctrl+Enter` | Result List | Reveals and selects the item in Windows File Explorer. |
| `Ctrl+Shift+Enter` | Result List | Launches the selected item with administrative privileges. |
| `Escape` | Quick Window | Hides the window. It never empties the box in that press — the query is kept or dropped by **Keep search box content after closing** instead. |
| `Escape` | Full Window | Clears the query and refocuses the box; closes the window when the box is already empty, or straight away when **Keep search box content after closing** is on (so you never press it twice for nothing). |
| `Escape` | Inline card | See **Summoning and Focus Handover in the Inline Window** above: it closes the card while the dialog holds the keyboard, and hands focus back while the card holds it. |
| `Escape` | Action Menu | One press per level: clears the action filter, then steps back out of the submenu, then leaves the menu and restores the original query. |
| `Backspace` | Action Menu | Exits the action menu back to the search list when filter text is empty. |
| `←` / `→` Arrow Keys | Action Menu | Left arrow navigates back to parent menu; right arrow enters submenus. `Tab` enters a submenu too, unless you have bound it to the next/previous-item hotkey. |
| `Tab` | Inline card | Swallowed so focus cannot leave the search box, while `Tab` inside the dialog itself keeps traversing its own controls as usual. |
| `Tab` | Action Menu | Enters the highlighted submenu — unless you have bound `Tab` to the next/previous-item hotkey, which wins. |
| `Apps` / `Shift+F10` | Full Window | Opens the action menu for the current selection, exactly like right-click. |
| `Alt+Space` | All Lertaro Windows | Suppressed to prevent triggering system titlebar menus on borderless windows. |
| `Alt+F4` | Full / Settings Windows | Closes window normally; suppressed on Quick, Inline, Quick Panel, Preview, LocalSend and in-app dialog windows. |

## 5. Plugin Action Hotkeys & Process Blacklist

### Plugin Action Hotkeys

Built-in file actions ship with these defaults: Cut `Ctrl+X`, Copy File `Ctrl+C`, Paste `Ctrl+V`, Delete `Delete`, Permanent Delete `Shift+Delete`, Copy Path `Ctrl+Shift+C`, Copy Name `Shift+C`, Reveal in Explorer `Ctrl+Enter`, Run as Administrator `Ctrl+Shift+Enter`.

Two guards make the destructive keys safe under a search box. A chord only reaches an action when the search box has **no text selected**, so `Ctrl+X` / `Ctrl+C` / `Ctrl+V` still cut, copy and paste your query as usual. A bare key like `Delete` only reaches an action when the caret is **already at the end** of the query, where it has no character left to delete; anywhere else in the text it stays a typing key. And both delete actions go through the shell's own `IFileOperation`, so the native "move to Recycle Bin?" / "permanently delete?" prompt still stands between the key and the files. Rebind or clear any of them under **Settings → Hotkeys → Plugin Actions**.

### Process Blacklist & Fullscreen Bypass

- **Automatic Fullscreen Bypass**: When a focused foreground application runs in exclusive fullscreen mode (e.g. 3D games or video players), Lertaro automatically bypasses all global hotkeys to avoid interrupting gameplay.
- **Custom Process Blacklist**: Add executable names under [**Settings → Hotkeys**](./settings/hotkeys-page#_3-process-blacklist) (e.g. `game.exe`) to silence hotkeys and mouse triggers while that process is focused.
