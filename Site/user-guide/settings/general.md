# General Settings

General Settings covers core application behaviors, search window dimensions and visual layouts, result type priority weights, and preview provider sequences. The page is divided into six top tabs: **System**, **Quick Search Window**, **Full Search Window**, **Preview Window**, **Quick Navigation**, and **Previews & Thumbnails**.

## 1. System

- **Start Lertaro on System Boot**: Automatically launches Lertaro upon Windows user login.
- **Check for Updates on Startup**: Automatically checks online for new releases whenever Lertaro starts.
- **Silent In-Place Updates**: Only available when running as administrator with "Check for Updates" enabled. Downloads and installs updates silently in the background without pop-up interruptions.
- **Enable Hardware Acceleration**: Enabled by default for Settings, Full Search, and Preview windows. Quick Search, inline cards, and notifications always use software rendering to reduce first-frame delay. If Lertaro prevents a dual-GPU laptop from switching to integrated graphics, disable this setting. Restart Lertaro to apply it.
- **Hide System Tray Icon**: Hides the icon from the Windows taskbar notification area. The logo inside the Quick Search bar continues to provide the full context menu, so access is never lost.
- **Enable Everything Compatibility Service (IPC)**: Emulates the standard Everything Win32 IPC protocol in the background. Third-party software (such as Directory Opus and Total Commander) can query Lertaro's in-memory index directly.
- **Enable Fuzzy Matching**: Enabled by default. When active, queries match non-contiguous character sequences. When disabled, queries require contiguous substring matches (see [**Search Syntax**](../search-syntax)). Takes effect immediately.
- **Keep Search Box Content after Closing**: Disabled by default. When enabled, closing Quick Search or pressing `Esc` in the Full Window keeps the query; `Esc` closes the Full Window on its first press. Reopened queries are **fully selected**, ready to replace by typing. The text is kept for this session only.
- **Show Currently Open Folders in Inline Search**: When the inline window is docked in a file dialog, shows the folders currently open in that dialog when the search query is empty. Enabled by default.
- **Inline Window Width (%)**: Accepts `20`–`100`. Values other than the default `60` specify a percentage of the host window width. `60` selects automatic sizing: the larger of 40% of the host width or one fifth of the screen. The final width is bounded below by the larger of 20% of the host or one sixth of the screen, and above by the smaller of the host width or 90% of the screen.
- **Inline Window Rows (Ordinary Window)**: Show 4–10 results when docked to a regular window (default: 10). Section headers do not count. Ctrl+1–9 selects results 1–9; Ctrl+0 selects result 10. Scroll to see rows that do not fit on screen.
- **Inline Window Rows (File Dialog)**: Shows `4`–`10` results in Open, Save, or Browse dialogs; default `4`. Section headers, navigation shortcuts, and screen-height limits work as above.
- **Plugin Query Token Prefix**: The plugin token prefix is configured in Settings > General > System. Only `\` is currently supported. Newly entered invalid values block saving; unchanged legacy values show a warning. Blank or unusable saved values use `\` at runtime.
- **Log Level**: Dropdown selecting Error / Warning / Info (default) / Debug, controlling log verbosity across all processes.
- **UI Language**: Selects the active display language across the entire application.

## 2. Quick Search Window

Fine-tunes the dimensions, layout, and priority rankings of the centered floating search bar:

### Search Bar Layout

- **Search Bar Width (Pixels)**: Range `300–1200px`, default `570px`.
- **Search Bar Height (Pixels)**: Range `45–120px`, default `60px`. This value proportionally scales result icon sizes, line heights, and typography for visual balance.
- **Show Clock in Search Box**: Replaces the placeholder text with current date and time when the search box is empty. The clock disappears as soon as you type.
- **Switch to Full Window on Second Hotkey Press**: When enabled, pressing the global summon hotkey while the Quick Window is already open transitions directly into the Full Window, carrying over your active query.
- **Lock Position**: Prevents dragging the search bar to avoid accidental displacement.
- **Automatically Fill Clipboard Text**: Disabled by default. When the Quick Search Window opens without pre-filled text, new non-empty clipboard text is used as the search query. Manual paste and other windows are unaffected.
- **Show the Type Filter Bar**: Enabled by default. Show the type tabs below the Quick Search box. Turning them off resets the filter to All and gives the result list more space.
- **Remember the Type Filter Bar's Last Tab**: Disabled by default. Keep the selected type tab when reopening Quick Search. It resets to All when Lertaro restarts.
- **Reset Layout Settings**: Restores all search bar layout properties to initial defaults.

### Result Type Priority & Trigger Characters

- **Priority Sorting List**: Drag Applications, System Settings, Files, and Folders into the preferred order. This breaks ties after match grade, priority, usage, and placement. Putting applications or settings above files/folders also makes exact names in those lower groups compete as strong matches. See [Search Syntax](../search-syntax) for examples.
- **Exclusive Single-Character Trigger**: Assign a dedicated character prefix (e.g. `;` for File Filters) to restrict searches exclusively to that type when typed at the start of a query. A trigger cannot be one of the characters the search syntax reads itself (`\` `<` `>` `:` `*` `/` `?`), because the syntax consumes it before the trigger is ever consulted and the trigger would silently stop working; two types also cannot share one character. The settings field reports both cases. The built-in instant answers also read their own leading characters -- `#` and `$` run a shell command, `%` searches environment variables -- so those are unavailable too.
- **Ranking Details Export**: In the quick window, right-click any result and choose **Export This Page's Ranking Details** to write one plaintext file for the whole displayed page. Nothing is logged in the background. The action is shown while **Enable ranking details export** is on (default).

## 3. Full Search Window

Configures default window geometry, columns, and sidebars for the main search window (`Ctrl+F`):

- **Window Width / Height (Pixels)**: Width range `640–2000px` (default `854px`), height range `400–1400px` (default `480px`).
- **Single Instance Only**: When enabled, invoking the Full Window focuses the existing instance instead of spawning duplicate windows.
- **Close on Hotkey Repeat**: Off by default, so pressing the global hotkey again cycles back to the Quick Search Window carrying the current query. When enabled, pressing it again while the Full Window has focus closes the window outright instead. A visible but unfocused Full Window is brought back to the front either way.
- **Reset Search Window Settings**: Reverts default dimensions to factory settings.
- **Result Table Column Order**: Customize the display order of columns (Name, Path, Date Modified, etc.) in the tabular view.
- **Sidebar Filter Order**: Reorder filter groups in the left sidebar; each category dynamically displays live matching item counts.
- **Action Menu Group Order**: Reorder action groups inside the context action menu (`Ctrl+O`).

## 4. Preview Window

- **Preview Window Width / Height (Pixels)**: Width range `250–900px`, height range `250–1200px`.
- **Reset Preview Settings**: Reverts the QuickLook preview window to standard default proportions, automatically constrained within visible screen bounds.

## 5. Quick Navigation

- **Provider Order**: Drag and reorder the provider groups at the root of the Quick Navigation menu. Each row is one provider, such as Folder Cascader and System Command Runner, plus the bookmark groups contributed by file manager plugins.

## 6. Previews & Thumbnails

- **File Preview Provider Order**: Adjust the execution sequence and fallback order for preview renderers (built-in media decoders vs. third-party QuickLook bridge).
- **Thumbnail Provider Order**: Adjust the resolution priority for file icon and thumbnail extraction providers.
