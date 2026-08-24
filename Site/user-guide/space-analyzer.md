# Space Analyzer

Lertaro includes a blazing-fast disk and directory **Space Analyzer**. Unlike traditional disk analyzers that require lengthy physical drive scans, it leverages Lertaro's existing in-memory index tree to render storage breakdowns instantly in sub-seconds — even on drives hosting millions of files.

## 1. Accessing Space Analyzer

- **Automatic Presentation**: Open the Full Search Window (`Ctrl+F`) with an **empty search box**; Space Analyzer appears automatically as the default home view.
- **Seamless Transition**: Typing any character in the search box instantly switches to the search results list; clearing the search box returns immediately to the Space Analyzer view.

## 2. Layout & Visual Overview

Space Analyzer uses a dual-pane layout designed to provide immediate clarity on storage usage:

### Left Pane: Treemap Visualization

- **Proportional Area**: Larger rectangles correspond to folders or files occupying more storage.
- **Color & Shade Depth**: Shading reflects relative size weight within the current parent directory, with distinct borders differentiating folders from individual files.

### Right Pane: Sorted Size Breakdown

- **Descending Order**: Lists child items sorted by size from largest to smallest, making storage hogs immediately identifiable.
- **Percentage Progress Bar**: Each row features a subtle proportion bar indicating its share of total visible storage.
- **Bidirectional Highlighting**: Selecting an item in the Treemap or the right-hand list synchronizes focus across both panes.
- **Smooth Name Scrolling**: Truncated long names scroll smoothly on hover or selection, replacing disruptive tooltip popups.

## 3. Navigation & Context Operations

- **Drill Down & Navigate**:
  - **Enter Subdirectory**: Double-left-click any Treemap card or list item to drill down into that directory.
  - **Navigate Up**: Click the "Up" arrow in the top navigation bar, or click any parent segment in the breadcrumb path.
- **Context Action Menu**: Right-click any card or row to open the standard **Action Menu** (Open, Copy Full Path, Reveal in Explorer, Recycle, or Permanent Delete).
- **Reveal with Middle-click**: Middle-click any item to reveal and locate it directly in your configured file manager.
- **Live Preview Sync**: Press `Alt+P` to open the QuickLook preview panel; the preview dynamically updates as you navigate across items.

## 4. Metrics Scope & Real-Time Sync

### Scope & Size Calculations

- **Index-Backed Breakdown**: Summarizes items already indexed by Lertaro without initiating disk I/O crawls. Excluded files do not count toward totals.
- **Logical File Sizes**: Shows actual logical file sizes; hard-linked data is counted once to prevent inflated sizes.
- **Hidden & System Items**: Hidden items are included normally; system files are merged into their parent folder's total size.

### Real-Time Change Tracking & Self-Healing

- **Live Change Updates**: Receives filesystem change notifications from the background index service, debouncing updates smoothly.
- **Self-Healing Path Fallback**: If the active directory is renamed, deleted, or removed from indexing, Space Analyzer intelligently steps back to the nearest valid parent folder without crashing.
