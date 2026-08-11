# Space Analyzer

The Space Analyzer turns Lertaro's existing file indexes into a fast, SpaceSniffer-style overview. It does not scan your disks, so it can open quickly even when several drives contain millions of entries.

## Open the analyzer

Right-click the Lertaro tray icon and choose **Space Analyzer**. The first level shows every index currently loaded by Lertaro. Double-click a drive or folder with the left mouse button to enter it; use the up arrow or any breadcrumb to go back.

## Read and use the view

- The treemap on the left gives larger items more area. Light and dark shades indicate relative size, while different borders distinguish folders from files.
- The list on the right shows the same items in descending size order. Selecting an item in either view highlights it in both.
- Right-click a card or list row to open the same actions menu used by search results, including opening, locating, copying, and any applicable plugin actions.
- Tooltips appear only when the name or size has been truncated.

## What is counted

Only entries already present in Lertaro's enabled indexes are included, and the analyzer never fills gaps by walking the filesystem. Excluded and unindexed content is absent; hidden and system entries are not shown as individual items, although their size can still contribute to a visible ancestor folder's total.

Sizes are logical file sizes rather than allocated disk usage. Directory totals include their indexed descendants, and hard-linked file data is counted only once, so totals can differ from Windows Explorer or a sector-level disk analyzer.

Opening the window and pressing **Refresh** query the latest in-memory index state, including file changes already received by the live monitors. The analyzer does not separately open or reload index cache files. Closing the window releases its rendered items and shared UI caches.
