# Actions & Instant Preview

Lertaro does more than find files at blazing speeds — it integrates a comprehensive context action system and a rich instant preview panel, letting you inspect, manage, and dispatch files without switching to File Explorer.

## 1. Action Menu Deep Dive

Press `Ctrl+O` or `→` on any search result (file, folder, application, or plugin entry) to expand the contextual action menu.

### Built-in Core Actions Cheat Sheet

| Action | Default Hotkey | Description |
| :--- | :--- | :--- |
| **Open** | `Enter` | Opens the selected item or launches the application with the system default program. |
| **Reveal in Explorer** | `Ctrl+Enter` | Opens the parent directory and highlights the item in Windows File Explorer. |
| **Run as Administrator** | `Ctrl+Shift+Enter` | Launches the selected application or script with elevated administrative permissions. |
| **Copy Full Path** | `Ctrl+Shift+C` | Copies the absolute path (e.g. `D:\Projects\app.exe`) to the clipboard. |
| **Cut / Copy File** | `Ctrl+X` / `Ctrl+C` | Places the file itself on the clipboard, ready to paste into Explorer or any folder. |
| **Paste into Folder** | `Ctrl+V` | When a folder is highlighted, pastes clipboard files directly into that directory. |
| **Delete (Recycle Bin)** | `Delete` | Safely moves the selected file or directory to the Windows Recycle Bin. |
| **Permanent Delete** | `Shift+Delete` | Permanently deletes the selected item (prompts for confirmation; cannot be recovered). |
| **Windows Context Menu** | — | Expands the full native Windows Explorer context menu with third-party extensions and "Send to". |

### Action Menu Interaction & Filtering

- **Type to Filter**: Once the action menu opens, type immediately to filter actions by name (e.g., typing `copy` narrows the list to copy-related actions).
- **Hierarchical Navigation**: On items with submenus (such as "Send to"), press `→` or `Enter` to enter; press `←` or `Backspace` (when filter text is empty) to return to the parent level.
- **Exit Menu**: Press `Escape` to close the action menu and return to the main search result list.

## 2. Full Window Results List Features

The Full Search Window (`Ctrl+F`) is designed for high-density file management and exploration:

- **Double-click Path Column**: Double-clicking the **Name** column opens the file; double-clicking the **Path** column opens the containing parent folder directly.
- **Infinite Streaming Results**: When scanning millions of items, results stream into the view incrementally without waiting for the full index scan to conclude. You can interact with rows immediately as they arrive.
- **Wrap-around Navigation**: Pressing `↑` on the top row wraps around to the last item; pressing `↓` on the bottom row wraps back to the first.
- **Window Dragging & Size Memory**: Drag the non-interactive top area of the window to reposition it; manually resized dimensions are automatically remembered across sessions.

## 3. Built-in QuickLook Instant Preview

Press `Alt+P` on any result to summon the docked preview panel alongside the search window:

### Supported Formats & Rich Capabilities

- **Images & Vector Graphics**: Crisp rendering and scaling for JPG, PNG, GIF (animated playback), BMP, WebP, ICO, SVG, and more.
- **Documents & Code Syntax**: Highlighting and formatting for TXT, Markdown, JSON, XML, YAML, C#, Python, JS, HTML, etc.
- **Audio & Video Playback**: Media files (MP4, MKV, AVI, MOV, WMV, MP3, WAV, FLAC, WMA) **auto-play immediately** with a theme-aware mini playback bar (play/pause, progress scrubbing, duration, mute). Playback stops instantly when switching items.
- **Folder Structural Inspection**: Shows up to 30 direct child items with file icons and sizes, automatically filtering system and hidden files.

### Adaptive Layout & Pop-up Handling

- **Adaptive Screen Bounds**: Preview dimensions can be customized under [**Settings → General → Preview**](./settings/general#preview); Lertaro guarantees the panel remains within the visible monitor bounds.
- **Native Dialog Avoidance**: When previewing password-protected Office documents, Lertaro temporarily hides both windows so the native password dialog can be interacted with, restoring seamlessly afterwards.
- **Drag Source**: The top area of the preview panel acts as a drag source — drag the previewed file directly into editors, browsers, or chat applications.

## 4. Plugin Interactive & Rich Text Previews

QuickLook supports custom interactive preview cards provided by plugins:

- **Theme-Adaptive Rich Text**: Rendered via modern WebView2 and native controls, automatically matching dark/light system themes with high-contrast typography and subtle translucent scrollbars.
- **Interactive Plugin Cards**: MDict dictionary lookups, live weather forecasts, instant webpage snapshots, and API debugging payloads.

## 5. Third-party QuickLook Bridge (Optional)

If you have installed the standalone open-source tool **QuickLook** ([QL-Win/QuickLook on GitHub](https://github.com/QL-Win/QuickLook)), enable the **QuickLook Bridge** plugin under [**Settings → Plugins**](./settings/plugins).

- **External Preview Takeover**: Connects via local named pipes to host external QuickLook preview windows anchored directly beside Lertaro.
- **Seamless Fallback**: If the external QuickLook process is not running, Lertaro smoothly falls back to its built-in preview engine.

## 6. Release File Occupation

The official **File Occupation Release** plugin adds a single-selection action for existing files. It lists the processes currently using the file, including their PIDs and executable paths, and sends a request for those processes to release it. The action is disabled for folders, missing files, or multiple selections; the release button is also disabled when no process is detected. The themed dialog supports refresh and automatically hides itself from Alt+Tab while remaining above the search window.

## 7. Add to Favorites

CoreExtensions provides an **Add to Favorites** action for one existing file or folder. It opens a themed dialog for the display name and hides the action when the same path is already a favorite.
