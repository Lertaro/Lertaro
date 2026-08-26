# Quick Panel Settings

The **Quick Panel** is a floating workspace designed for high-frequency file retrieval, project context management, and drag-and-drop file staging. When invoked, it docks automatically to the bottom-right corner of the active foreground window, allowing you to access and stage files without switching contexts.

## 1. Core Architecture & Global Toggle

- **Enable Quick Panel**: Master switch; when disabled, the hotkey does not intercept input.
- **Summon Hotkey**: Default **`Ctrl+F2`** (customizable under [**Settings → Hotkeys**](./hotkeys-page)).
- **Smart Docking & Sizing**: Sized by default to half the width and height of the host window (constrained to a minimum of `280 × 200px` to guarantee legibility). If a Lertaro window is already focused, summon requests are ignored to avoid stacking; pressing the hotkey while the panel is open closes it.
- **Quick Jump Integration**: While open, the physical folder of the active group is tracked as the working directory. Pressing Quick Jump (`Ctrl+G`) in file dialogs navigates straight there.

## 2. Workspaces

Workspaces organize folders by project or task:

- **Workspace Management**: The left list manages workspaces (**New**, **Duplicate**, **Delete**), supporting drag-and-drop reordering that maps directly to the horizontal tab bar.
- **Properties**:
  - **Name**: Text shown on the tab header (falls back to a localized default name if blank).
  - **Enable Switch**: Toggles visibility in the tab bar. Clicking the **×** button on a tab in the panel hides it; re-enable it here.

Selected workspaces are configured across two tabs: **Sources** and **Applications**.

## 3. Sources Configuration

Each source represents a distinct group within the workspace:

- **Add Folder**: Selects the target directory on disk.
- **Display Mode**:
  - **Recent modified files** — Queries the memory index in sub-milliseconds to show recently changed files (newest first).
  - **All files, newest first** — Shows all files sorted by last modified date in descending order.
  - **All files, by name** — Functions as a pinned shortcut launcher.
- **Include Subfolders**: Recursively includes descendant files when checked.
- **Accept Dropped Files**: Allows dragging files, folders, or web images from other windows into this group. Lertaro executes a native Windows copy with conflict prompts and undo support.
- **Filtering Rules**: Uses wildcard patterns or search-syntax `@` filters to constrain displayed file types (e.g. `*.mp4;*.mkv`, `*.lnk;:@doc;:@img`, or `*.lnk;:@doc|img`).
- **Max Count & Time Limit**: Limits total items displayed (0 for unlimited) or restricts results to files modified within N minutes.
- **Detailed List vs. Thumbnail Tiles**: Choose between a compact list view or thumbnail grid (tiles scale proportionally while preserving image aspect ratios).

## 4. Plugin Tabs

Plugins can register global dynamic lists into the Quick Panel. The CoreExtensions plugin provides five built-in tabs:

| Plugin Tab | Contents |
| :--- | :--- |
| **Favorites** | Pinned starred items; URLs open directly in your default browser. |
| **History** | Items and applications recently launched through Lertaro. |
| **Windows History** | Resolves Windows Recent Documents into physical file targets. |
| **Last Directory** | Tracks the folder you just navigated in File Explorer or a file dialog. |
| **Recent Files** | Aggregates the newest files across all configured folders using the memory index. |

Each plugin tab can be enabled/disabled and configured with List or Tile views.

## 5. Application Binding & Dedicated Blacklist

- **Applications**: Associate workspace tabs with foreground process names (e.g. `chrome.exe` or `devenv.exe`). Summoning the Quick Panel over those apps switches directly to that workspace.
- **Quick Panel Only Blacklist**: Configure apps where the Quick Panel should not appear. This list is **additive** to the global blacklist.

## 6. Panel Navigation Guide

- **Live Fuzzy Filtering**: A search box in the top-right corner supports fzf fuzzy matching and pinyin aliases to filter the active workspace.
- **Keyboard Navigation**: Arrow keys navigate seamlessly across group boundaries; press `Enter` to open the highlighted item.
- **Tab Switching**: Press `Ctrl` + `1`–`9` to jump directly across workspace and plugin tabs.
- **QuickLook & Pinning**: Press `Alt+P` to open the docked instant preview; press `Ctrl+T` to pin the panel so it stays open when focus is lost.
