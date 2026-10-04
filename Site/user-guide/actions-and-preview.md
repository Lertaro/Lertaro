# Actions & Instant Preview

Lertaro locates files and integrates a context action system and an instant preview panel, letting you inspect, manage, and dispatch files without constantly switching to File Explorer.

## 1. Action Menu Deep Dive

In the Quick Window, press `Ctrl+O` or `→` on the highlighted result to expand the contextual action menu — `→` only when the caret is already at the end of the query, so it never hijacks cursor movement while you are editing earlier in the text. The same keys work in the inline card **outside** file dialogs; a card docked in an Open/Save/Browse dialog has no action menu at all. In the Full Search Window neither key is wired: right-click a result, or press `Apps` (`Shift+F10`), to open the same menu there.

Not every row has a menu. It is withheld from the **Show More** row, plugin-provided results, web-link favorites, instant results that no provider claims, applications outside the Quick Window, and every file dialog — on such a row the menu keys do nothing instead of opening an empty panel.

### Built-in Core Actions Cheat Sheet

| Action | Default Hotkey | Description |
| :--- | :--- | :--- |
| **Open** | `Enter` | Opens the selected item or launches the application with the system default program. In the Full Search Window, `Enter` acts on **every** selected row; double-click opens only the row you double-clicked. |
| **Reveal in Explorer** | `Ctrl+Enter` | Opens the parent directory and highlights the item in Windows File Explorer. |
| **Run as Administrator** | `Ctrl+Shift+Enter` | Launches the selected application or script with elevated administrative permissions. |
| **Copy Full Path** | `Ctrl+Shift+C` | Copies the absolute path (e.g. `D:\Projects\app.exe`) to the clipboard. |
| **Copy Name** | `Shift+C` | Copies the names of the selected files or folders to the clipboard, without their paths. |
| **Copy File** | `Ctrl+C` | Places the file itself on the clipboard, ready to paste into Explorer or any folder. |
| **Cut / Copy File** | `Ctrl+X` / `Ctrl+C` | Places the file itself on the clipboard, ready to paste into Explorer or any folder. With text selected in the search box these stay text commands. |
| **Paste into Folder** | `Ctrl+V` | When a folder is highlighted, pastes clipboard files directly into that directory; when a file is highlighted, pastes into its parent folder. Needs a real file list on the clipboard, so pasting text into the query is unaffected. |
| **Delete (Recycle Bin)** | `Delete` | Safely moves the selected file or directory to the Windows Recycle Bin. A bare key only reaches the action when the caret is already at the end of the query, and the native Recycle Bin confirmation follows. |
| **Permanent Delete** | `Shift+Delete` | Permanently deletes the selected item (native "permanently delete?" prompt; after that it cannot be recovered). |
| **Rename** | — | Renames one existing file or folder through the Windows Shell. The dialog preselects the filename portion for convenient replacement. |
| **Windows Context Menu** | — | Expands the full native Windows Explorer context menu with third-party extensions and "Send to". |

### Action Menu Interaction & Filtering

- **Type to Filter**: Once the action menu opens, type immediately to filter actions by name (e.g., typing `copy` narrows the list to copy-related actions). The filter box takes the keyboard, so navigating away never touches your original query.
- **Mnemonic Letters**: Every action may show a highlighted letter. Pressing it **runs the action at once** — it does not merely highlight it — but only while the filter box is still empty, so the first letter you type for a filter is never stolen.
- **Independent Search Box**: The action menu has its own focused search box, so filtering actions never changes the main search query. Moving to another menu level clears the action filter and focuses the new level's search box.
- **Floating Action Panel**: In the Quick Window, Quick Launch panel, and Full Search Window, actions appear in a floating panel anchored to the active result. The Quick Launch panel expands to the action menu's full working height and returns to its compact height when the menu closes.
- **Hierarchical Navigation**: On items with submenus (such as "Send to"), press `→`, `Tab` or `Enter` to enter; press `←` or `Backspace` (when filter text is empty) to return to the parent level. In a nested menu, `Escape` and right-click return to the parent; at the root level they close the action menu.
- **Navigation Keys Still Work**: Your configured Next/Previous Item hotkeys (`Ctrl+N` / `Ctrl+P` by default) move the highlight inside the action list too, wrapping past the first and last entry and skipping separators, headers and disabled actions. `Tab` only enters a submenu when you have not bound it to one of those keys.
- **Click Away to Close**: Clicking outside a floating action panel closes it. Right-clicking another result replaces the current action target in place when the host supports it.
- **Action Hotkeys**: Provider-defined action shortcuts work while the action panel is focused. Executing one closes the floating panel while keeping the Full Search Window or Quick Launch panel open.

## 2. Full Window Results List Features

The Full Search Window (`Ctrl+F`) is designed for high-density file management and exploration:

- **Double-click Path Column**: Double-clicking the **Name** column opens the file; double-clicking the **Path** column opens the containing parent folder directly. Double-click means the *left* button only; double-clicking a column header does nothing, and maximize/restore is the double-click on the window's top non-input band.
- **Multi-Selection**: The grid is a normal Windows list — `Ctrl`+click adds or removes rows, `Shift`+click takes a range, and `Enter` then acts on **all** selected rows at once, while a double-click opens only the row you double-clicked. Right-clicking a row that is already part of a selection keeps the whole selection instead of collapsing it to that one row.
- **Infinite Streaming Results**: When scanning millions of items, results stream into the view incrementally without waiting for the full index scan to conclude. You can interact with rows immediately as they arrive, and appending new rows keeps your selection and scroll position; a genuinely new result set starts again from the top.
- **Wrap-around Navigation**: Pressing `↑` on the top row wraps around to the last item; pressing `↓` on the bottom row wraps back to the first. The same wrap applies in the Quick Window and the inline card — pressing `↑` with nothing selected yet lands on the last row, `↓` on the first. Only the Quick Panel walks its groups as one continuous list **without** wrapping.
- **Horizontal Scrolling**: `Shift` + mouse wheel over the grid scrolls three columns per notch, which is how you reach the far columns on a narrow window.
- **Drag Files Out**: Any row can be dragged straight into Explorer, a dialog, or a chat window as a real file drop; with a multi-selection, dragging one of its members carries **all** of them. Dropping outside Lertaro hides the window, and the close is held back while a drag is in flight so the drag cursor cannot stick.
- **Selection Summary**: When multiple rows are selected, the status bar shows the selected item count next to the total result count.
- **Hover Previews**: Moving the pointer over a row retargets an already-open preview panel without changing the selection, so you can flip through candidates and press `Enter` on the row you came back to.
- **Window Dragging & Size Memory**: Drag the non-interactive top area of the window to reposition it; manually resized dimensions are automatically remembered across sessions.

## 3. Built-in QuickLook Instant Preview

Press `Alt+P` on a previewable result to summon the docked preview panel alongside the search window — or **middle-click the row**, which toggles the same panel. Neither gesture is available in the inline window; the Quick Panel supports both.

### Preview Has No Keyboard

The panel is deliberately non-activating: it never takes focus from the search window, so there is nothing to type into and `Escape` does not close it. Toggle it with `Alt+P` or hide it by closing the window it is docked to. All its controls — the media playback bar, scrollbars, plugin cards — are mouse-only, and keyboard focus stays exactly where it was.

### Supported Formats & Rich Capabilities

- **Images**: Scaling render for JPG, PNG, GIF, BMP, and ICO images.
- **Text & Code**: Plain monospaced rendering for TXT, Markdown, JSON, XML, YAML, C#, Python, JS, HTML, etc. (no syntax highlighting).
- **Audio & Video Playback**: Video files (MP4, M4V, WMV, AVI, MOV, MPG/MPEG) and audio files (MP3, WAV, WMA, M4A, AAC) **auto-play immediately** with a theme-aware mini playback bar (play/pause, progress scrubbing, duration, mute). Playback stops instantly when switching items.
- **Folder Structural Inspection**: Shows up to 30 direct child items with file icons and sizes, automatically filtering system and hidden files.

### Adaptive Layout & Pop-up Handling

- **Adaptive Screen Bounds**: Preview dimensions can be customized under [**Settings → General → Preview**](./settings/general#_4-preview-window); Lertaro guarantees the panel remains within the visible monitor bounds.
- **Docking Side**: The panel docks to the **right** of its search window and flips to the left only when the right cannot fit it — it does not chase the roomier side, so it stays put while you scroll a wide window. It then follows the owner as you move or resize that window.
- **Resize Memory Is Per Session**: Dragging the resize grip or moving the panel is remembered while the preview stays in play, but the next time the search window is hidden or closed the panel returns to the configured size and docking side.
- **Native Dialog Avoidance**: When previewing password-protected Office documents, Lertaro temporarily hides both windows so the native password dialog can be interacted with, and restores them afterwards.
- **Drag Source**: The top area of the preview panel acts as a drag source — drag the previewed file directly into editors, browsers, or chat applications. The footer bar drags the panel itself.

## 4. Plugin Interactive & Rich Text Previews

QuickLook supports custom interactive preview cards provided by plugins:

- **Theme-Adaptive Rich Text**: Rendered via modern WebView2 and native controls, automatically matching dark/light system themes with high-contrast typography and subtle translucent scrollbars.
- **Interactive Plugin Cards**: MDict dictionary lookups, live weather forecasts, instant webpage snapshots, and API debugging payloads.

## 5. Third-party QuickLook Bridge (Optional)

If you have installed the standalone open-source tool **QuickLook** ([QL-Win/QuickLook on GitHub](https://github.com/QL-Win/QuickLook)), enable the **QuickLook Bridge** plugin under [**Settings → Plugins**](./settings/plugins).

- **External Preview Takeover**: Connects via local named pipes to host external QuickLook preview windows anchored directly beside Lertaro.
- **Automatic Fallback**: If the external QuickLook process is not running, Lertaro falls back to its built-in preview engine.

## 6. Release File Occupation

The official **File Occupation Release** plugin adds a single-selection action for existing files. It lists the processes currently using the file, including their PIDs and executable paths, and sends a request for those processes to release it. The action is disabled for folders, missing files, or multiple selections; the release button is also disabled when no process is detected. The themed dialog supports refresh and automatically hides itself from Alt+Tab while remaining above the search window.

## 7. Add to Favorites

CoreExtensions provides an **Add to Favorites** action for one existing file or folder. It opens a themed dialog for the display name and hides the action when the same path is already a favorite.

## 8. Rename

CoreExtensions provides a **Rename** action for one existing file or folder. The themed dialog shows the full current name and preselects the filename portion: for `report.pdf`, only `report` is selected, while folders and names without an extension are selected in full. Press `Enter` to confirm or `Esc` to cancel. After confirmation, the actual rename is delegated to the Windows Shell.
