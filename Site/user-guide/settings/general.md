# General Settings

General Settings covers core application behaviors, search window dimensions and visual layouts, result type priority weights, and preview provider sequences. The page is divided into six top tabs: **System**, **Quick Search Window**, **Full Search Window**, **Preview Window**, **Quick Navigation**, and **Previews & Thumbnails**.

## 1. System

- **Start Lertaro on System Boot**: Automatically launches Lertaro upon Windows user login.
- **Check for Updates on Startup**: Automatically checks online for new releases whenever Lertaro starts.
- **Silent In-Place Updates**: Only available when "Check for Updates" is enabled. Downloads and installs updates silently in the background without pop-up interruptions.
- **Enable Hardware Acceleration**: Enabled by default. If your dual-GPU laptop (e.g. NVIDIA Advanced Optimus) fails to switch graphics cards because Lertaro is active, disable this to use software rendering. Requires restarting Lertaro.
- **Hide System Tray Icon**: Hides the icon from the Windows taskbar notification area. The logo inside the Quick Search bar continues to provide the full context menu, so access is never lost.
- **Enable Everything Compatibility Service (IPC)**: Emulates the standard Everything Win32 IPC protocol in the background. Third-party software (such as Directory Opus and Total Commander) can query Lertaro's in-memory index directly.
- **Enable Fuzzy Matching**: Enabled by default. When active, queries match non-contiguous character sequences. When disabled, queries require contiguous substring matches (see [**Search Syntax**](../search-syntax)). Takes effect immediately.
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

- **Priority Sorting List**: Drag or move items (Applications, System Settings, Files, Plugin Extensions) to adjust which types rank highest in search results.
- **Exclusive Single-Character Trigger**: Assign a dedicated character prefix (e.g. `;` for File Filters) to restrict searches exclusively to that type when typed at the start of a query.

## 3. Full Search Window

Configures default window geometry, columns, and sidebars for the main search window (`Ctrl+F`):

- **Window Width / Height (Pixels)**: Width range `640–2000px` (default `854px`), height range `400–1400px` (default `480px`).
- **Single Instance Only**: When enabled, invoking the Full Window focuses the existing instance instead of spawning duplicate windows.
- **Reset Search Window Settings**: Reverts default dimensions to factory settings.
- **Result Table Column Order**: Customize the display order of columns (Name, Path, Date Modified, etc.) in the tabular view.
- **Sidebar Filter Order**: Reorder filter groups in the left sidebar; each category dynamically displays live matching item counts.
- **Action Menu Group Order**: Reorder action groups inside the context action menu (`Ctrl+O`).

## 4. Preview Window

- **Preview Window Width / Height (Pixels)**: Width range `250–900px`, height range `250–1200px`.
- **Reset Preview Settings**: Reverts the QuickLook preview window to standard default proportions, automatically constrained within visible screen bounds.

## 5. Quick Navigation

- **Provider Order**: Drag and reorder root categories in the Quick Navigation menu (Favorites, History, Open Folders, and third-party file manager bookmarks).

## 6. Previews & Thumbnails

- **File Preview Provider Order**: Adjust the execution sequence and fallback order for preview renderers (built-in media decoders vs. third-party QuickLook bridge).
- **Thumbnail Provider Order**: Adjust the resolution priority for file icon and thumbnail extraction providers.
