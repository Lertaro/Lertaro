# General Settings

General Settings covers core application behaviors, search window dimensions and visual layouts, result type priority weights, and preview provider sequences. The page is divided into six top tabs: **System**, **Quick Search Window**, **Full Search Window**, **Preview Window**, **Quick Navigation**, and **Previews & Thumbnails**.

## 1. System

- **Start Lertaro on System Boot**: Automatically launches Lertaro upon Windows user login.
- **Check for Updates on Startup**: Automatically checks online for new releases whenever Lertaro starts.
- **Silent In-Place Updates**: Only available when running as administrator with "Check for Updates" enabled. Downloads and installs updates silently in the background without pop-up interruptions.
- **Enable Hardware Acceleration**: Enabled by default. It applies to the windows you stay inside (Settings, the Full Search Window, QuickLook); the windows you only summon — quick search, the inline card and notifications — are always rendered in software, which is what lets them appear already painted instead of as an empty frame. If a dual-GPU laptop (e.g. NVIDIA Advanced Optimus) cannot switch graphics because Lertaro stays resident, disable this to render every window in software. Requires restarting Lertaro.
- **Hide System Tray Icon**: Hides the icon from the Windows taskbar notification area. The logo inside the Quick Search bar continues to provide the full context menu, so access is never lost.
- **Enable Everything Compatibility Service (IPC)**: Emulates the standard Everything Win32 IPC protocol in the background. Third-party software (such as Directory Opus and Total Commander) can query Lertaro's in-memory index directly.
- **Enable Fuzzy Matching**: Enabled by default. When active, queries match non-contiguous character sequences. When disabled, queries require contiguous substring matches (see [**Search Syntax**](../search-syntax)). Takes effect immediately.
- **Keep Search Box Content after Closing**: Disabled by default. When enabled, neither hiding the Quick Window nor pressing `Escape` in the Full Window empties the box — and `Escape` in the Full Window closes the window on the first press instead of asking twice. The query a window is handed on its next summon arrives **selected whole**, so the first keystroke replaces it rather than appending. This lasts for the current run only; the text is never written to disk.
- **Show Currently Open Folders in Inline Search**: When the inline window is docked in a file dialog, shows the folders currently open in that dialog when the search query is empty. Enabled by default.
- **Inline Window Width (%)**: Text box taking `20`–`100`, default `60`. The inline window takes that percent of the width of the window it docks to. `60` stores no choice at all, so an untouched card takes the larger of 40% of that window or a fifth of the screen's width instead. The screen still bounds the result, never below the larger of 20% of that window or a sixth of the screen's width, and never above the smaller of that window itself or 90% of the screen's width. Lower it when the card covers too much of a maximized window.
- **Inline Window Rows (Ordinary Window)**: Text box taking `4`–`10`, default `10`. How many results the inline window shows while docked to an ordinary window. Section titles are not counted — they take their own rows on top of the number you set. `Ctrl+1`–`Ctrl+9` select the first through ninth result and `Ctrl+0` the tenth, which is why the box stops at 10. A screen too short for the count shows the rows that fit and scrolls.
- **Inline Window Rows (File Dialog)**: Same range, default `4`. The count for the card docked over a file dialog (Open/Save/Browse); a dialog is mostly other programs' controls, so that card starts shorter. Titles, shortcuts and the screen bound work as above.
- **Query Token Delimiter**: Single-character text box (default `:`). Defines the leading character for suffix tokens (e.g. `:.pdf`, `:@doc`, `:[S]`).
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
- **Reset Layout Settings**: Restores all search bar layout properties to initial defaults.

### Result Type Priority & Trigger Characters

- **Priority Sorting List**: Drag or move items (Applications, System Settings, Files, Plugin Extensions) to decide which types come first **among similarly relevant direct matches** without overriding stronger exact matches.
- **Exclusive Single-Character Trigger**: Assign a dedicated character prefix (e.g. `;` for File Filters) to restrict searches exclusively to that type when typed at the start of a query.
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
