# Space Analyzer

The Space Analyzer turns Lertaro's existing file indexes into a fast, SpaceSniffer-style overview. It does not scan your disks, so it can open quickly even when several drives contain millions of entries.

## Open the analyzer

Open Lertaro's full search window and leave the search box empty. Space Analyzer appears automatically as the window's home page and shows every index currently loaded by Lertaro. Typing a query switches to search results immediately; clearing the query returns to the analyzer at its root. Double-click a drive or folder with the left mouse button to enter it; use the up arrow or any breadcrumb to go back.

## Read and use the view

- The treemap on the left gives larger items more area. Light and dark shades indicate relative size, while different borders distinguish folders from files.
- The list on the right shows the same items in descending size order. A thin bar under each row shows its share of the current location's visible total. Selecting an item in either view highlights it in both.
- Right-click a card or list row to open the same actions menu used by search results, including opening, locating, copying, and any applicable plugin actions.
- Select a card or list row and use the configured preview shortcut to open the full search window's preview; an open preview follows subsequent selections.
- An overflowing name in the right-hand list scrolls while its row is selected or hovered, instead of showing a tooltip.

## What is counted

Only entries already present in Lertaro's enabled indexes are included, and the analyzer never fills gaps by walking the filesystem. Excluded and unindexed content is absent. Hidden entries are shown, while system entries are not shown individually, although their size can still contribute to a visible ancestor folder's total.

Sizes are logical file sizes rather than allocated disk usage. Directory totals include their indexed descendants, and hard-linked file data is counted only once, so totals can differ from Windows Explorer or a sector-level disk analyzer.

While the analyzer page is visible, it follows relevant in-memory index change events and coalesces bursts before updating. The root view reacts when a local or folder index is enabled, disabled, or removed. Inside a directory, only changes to that directory or its descendants rebuild the view; changes to an ancestor merely validate the current path. If the current directory or one of its ancestors is renamed, deleted, or disappears from the enabled indexes, the analyzer automatically returns to the nearest available ancestor. It never opens or reloads index cache files. Starting a search pauses analyzer updates; closing the full search window releases the rendered items and shared UI caches.
