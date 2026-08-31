# Quick Launch Settings

Quick Launch is the launch panel shown below the Quick Search Window when the search box is empty. It combines manually managed items with selected dynamic data sources such as Favorites and History.

## 1. Master switch

- **Enable quick launch panel**: Enables the panel. It appears only in the Quick Search Window when the query is empty and at least one source has available data.
- When disabled, no source is loaded for the panel.
- The panel height is based on the source with the most items and is capped by the Quick Search Window's maximum result area.

## 2. Manual quick-launch items

Open **Settings → Quick Launch → Quick Launch** to manage your own items:

- Add a file, folder, URL, or Windows shell path. Environment variables are supported when the target is checked.
- The display name is optional. If it is blank, Lertaro derives a name from the target.
- The file and folder browse buttons support selecting multiple items at once; each valid, non-duplicate target is added as its own entry.
- Use the drag handle to reorder items. Editing changes the same row into an inline editor with save and cancel controls; the remove control deletes the item.
- For a manual item, hover over the tile and click the ellipsis button to open its menu, then choose **Edit** or **Remove**. **Edit** opens the item editor dialog.
- When the list contains items, use **Clear list** beside the list heading to remove all manual items at once; the button is hidden when the list is empty.
- Missing targets are omitted from the panel until they become available again.

## 3. Data sources

Open **Settings → Quick Launch → Data sources** to choose dynamic sources:

- Sources come from the installed Quick Panel tab providers. This lets Quick Launch reuse Favorites, History, Windows History, Last Directory, Recent Files, and future plugin sources without maintaining a second copy of their data.
- A source is enabled by default when its provider is present. Only explicitly disabled providers are stored in user settings.
- A source gets a panel tab only when it is enabled and currently returns data. Empty sources are hidden.
- Use the drag handle beside a data source to reorder its panel tabs; the order is saved with the Quick Launch settings.
- If all manual items and dynamic sources are empty, the Quick Launch panel is hidden.

## 4. Panel interaction

- The number of columns adapts to the search-bar width.
- With one source, the source indicator is hidden.
- With multiple sources, the bottom strip shows one dot per source. The selected dot is blue and the other dots are gray; hovering over a dot expands it to show the source name.
- Hold **Shift** and scroll over the panel to cycle through sources. The selected source briefly plays the same reveal animation.
- The configured **Select Next Item** and **Select Previous Item** hotkeys (defaults **Ctrl+N** and **Ctrl+P**) also cycle through sources when the panel is visible; selection wraps from the last source to the first and vice versa.
- Right-click any item from any source to open the standard action menu for that item.
- When the panel contains more items than fit vertically, use the mouse wheel over the item area to scroll; hovering an item does not block vertical scrolling.
- Starting a query hides the panel.
