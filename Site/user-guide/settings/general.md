# General

Six tabs: **System**, **Quick Search Window**, **Full Search Window**, **Preview**, **Quick
Navigation**, and **Preview & Thumbnails**.

## System

- **Start Lertaro with Windows** — checkbox, launches Lertaro at sign-in.
- **Auto check for updates on startup** — checkbox.
- **Auto silent update when a new version is detected** — checkbox, only enabled while the check
  above is on; downloads and installs updates in the background without prompting.
- **Enable hardware acceleration** — checkbox, on by default. Turning it off forces the quick
  search window to render in software instead of using Direct3D — this works around NVIDIA
  Advanced Optimus refusing to hot-switch GPUs while Lertaro is running (only the quick window is
  affected, not the whole app). Requires restarting Lertaro to take effect.
- **Hide tray icon** — checkbox, off by default. Applies immediately, no restart needed. The same
  menu the tray icon's right-click shows is always available from the
  [Quick window's own logo](../hotkeys#search-box-logo-icon) regardless of this setting, so hiding
  the tray icon never strands you without a way back into Settings or Exit.
- **Open folders in new File Explorer tabs** — checkbox, off by default. On Windows 11, **Open** and **Open Containing Folder** use a new tab in an existing native File Explorer window; the latter also selects the requested item. If no File Explorer window exists, Windows opens the first folder in a normal window, then later folders use tabs. This experimental integration is ignored when a third-party file manager is configured.
- **Enable fuzzy matching** — checkbox, on by default. With fuzzy matching on, a bare search term
  matches as long as its characters occur in order anywhere in the name; turning this off requires
  a bare term to appear as a contiguous substring instead (`abc` no longer matches `a-b-c`) — see
  [Search Syntax](../search-syntax#fuzzy-matching-default) for what does and doesn't change.
  Applies immediately, no restart needed.
- **Global query token prefix** — text box (1 character max, default `:`). Sets the lead prefix character used to split query token expressions at the trailing position of the search box. Dynamic path filtering and custom filter providers also configure their prefix characters under **Settings → Plugins → CoreExtensions**.
- **Log level** — dropdown: Error / Warn / Info (default) / Debug. Controls verbosity across the
  App, Service, and Hook logs (see [Service Status](./service-status)).
- **Interface language** — dropdown, populated from every installed translation provider (built-in
  languages plus any a plugin adds).

Theme selection moved to its own [Appearance](./appearance) section — see that page for the theme
picker and the "follow system light/dark setting" option.

## Quick Search Window

Covers both the quick search bar's appearance and how its search results are prioritized.

**Search Bar Layout** — customizes the size and on-screen position of the quick search bar:

- **Search Bar Width (px)** — range 300–1200px, default 570px.
- **Search Bar Height (px)** — range 45–120px, default 60px. This one number also drives the
  result row's icon size, name/path font size, and row height (`height / 60`), so a taller search
  bar scales the whole result list up with it, always keeping the same proportions between icon and
  text.
- **Show clock in search box** — checkbox, off by default. While the search box is empty, replaces
  the usual "Type to search..." placeholder text with the current date, day of week, and time
  instead. Disappears the moment you start typing, same as the placeholder it replaces. Quick window
  only — the inline window always keeps its normal placeholder, even with this on.
- **Reopen as full window on repeat hotkey** — checkbox, off by default. Normally, pressing the
  toggle hotkey again while the quick window is already open just hides it; turning this on makes
  the second press switch to the full window instead (carrying over whatever query you'd already
  typed), rather than closing anything.
- **Lock position** — checkbox, off by default. Stops the quick window being dragged, so a stray
  press can't nudge it off the spot you put it on. Right-clicking the logo still resets its
  position, and **Reset Layout Settings** below clears the lock along with everything else.
- **Reset Layout Settings** button — restores all five settings above to their defaults.

Right-clicking the [quick window's logo](../hotkeys#search-box-logo-icon) resets just its on-screen
position (not size), re-centering it the same way it centers on first launch.

**Result Type Priority** — the same up/down-arrow (or drag-to-reorder) list used for [Quick
Navigation](#quick-navigation) below: move a result type (Applications, Settings, File Filters, any
third-party plugin's own searchable items, or the built-in "Files" entry) up or down to make it always
outrank the types below it in the quick window's results, regardless of which one actually matched the
query text better. History and Favorites always come first and aren't part of this list.

Each type can also have its own single-character **trigger** (optional, one text box per row): typing
that character as the very first thing in the quick window shows only that type's results, hiding
everything else — see [Search Syntax](../search-syntax#result-type-trigger) for examples. Typing just
the trigger with nothing after it yet shows a "keep typing to search X only" prompt instead of
"No Search Results". Pick a character you'd never normally start a real search with, since it's
reserved once a type is assigned it — a punctuation character (e.g. `;`) works more reliably than a
plain space, since a lone space with nothing typed after it is treated the same as an empty search
box and won't show that prompt.

## Full Search Window

Sets the default size of the full/main search window (the larger window you get from the taskbar
or Start Menu shortcut, as opposed to the quick popup — see
[Getting Started](../getting-started#the-three-windows)):

- **Window Width (px)** — range 640–2000px, default 854px.
- **Window Height (px)** — range 400–1400px, default 480px. The minimums match the window's own
  resize floor, so a configured value is never silently overridden by the window itself.
- **Reset Search Window Settings** button.
- **Only allow one full search window** — checkbox, off by default. When off, you can open several independent full windows. When on, opening the full window again activates an existing one instead of creating another; a query carried over from Quick window's **Show More** replaces that window's query.

Dragging the window's edge to resize it manually is remembered automatically — the next time you
open the window (or open a new one), it comes back at whatever size you last left it at, and this
page's fields update to match. Resizing while maximized doesn't overwrite the remembered size; only
resizing in the normal (non-maximized) state does.

The full window does not prepend Favorites or History recall entries to its result list. Those personalized recall results remain in the Quick and inline windows; the full window keeps the normal relevance order from active search providers.

**Results Grid Column Order** — the same reorder list used elsewhere in Settings (see
[Favorites](./favorites)): move a results-grid column (Name, Path, Date Modified, or any
plugin-provided column) left or right. Only affects the grid/table results layout — there are no
columns to reorder in the compact list layout.

**Sidebar Filter Order** — same mechanic, for the order the sidebar's filter groups (Type, Date
Modified, and any plugin-added groups) appear in.

While a full search is running, each sidebar filter shows a live count of matches in the current query. Counts update as results stream in and reset when the query changes; they are not affected by which sidebar filter is currently selected. The sidebar width follows its widest filter label and count, so labels remain visible without wrapping.

**Actions Menu Section Order** — same mechanic, for the order the sections of the
[actions menu](../actions-and-preview#actions-menu) appear in: the built-in actions group, plus one
section per plugin that contributes actions there (e.g. Custom Actions, or the Windows shell
right-click menu). A section not yet in this list falls back to its natural position (built-in
first, then plugin sections in whatever order they were contributed).

Clicking a results-grid column header cycles through three states: ascending, descending, then a
third click resets it back to the default relevance-ranked order (the header's sort arrow
disappears). This is remembered only for as long as Lertaro keeps running — quitting the app (not
just closing the window) resets sorting back to the default the next time you open it, unlike the
column/sidebar order above which is saved permanently.

## Preview

- **Preview Width (px)** — range 250–900px.
- **Preview Height (px)** — range 250–1200px, default sized so the pane isn't overly tall on a
  typical display.
- **Reset Preview Window Settings** button.

The preview window ignores the current result count — it's a fixed size, not a size that grows
with content. See [Actions Menu & Preview](../actions-and-preview) for how the pane is positioned.

## Quick Navigation

Sets the order the [Quick Navigation](../hotkeys#quick-navigation-mouse) menu's root-level sections
appear in — one section per contributing provider (e.g. Favorites/History/configured folders, Total
Commander's Directory Hotlist, Directory Opus's Favorites, a plugin's own quick-nav entries), each
labeled with its own header.

**Provider Order** — the same up/down-arrow (or drag-to-reorder) list used elsewhere in Settings (see
[Favorites](./favorites)): move a provider up or down to change where its section lands relative to
the others. Only providers whose plugin component is currently enabled are listed here — one
disabled under [Plugins](./plugins) never becomes a menu candidate in the first place, so there's
nothing to order for it.

## Preview & Thumbnails

Two independent priority-order lists, each deciding which provider gets first refusal for its own
job — normally decided purely by each provider's own fixed, built-in priority, with no way to change
that short of disabling a provider entirely.

**File Preview Provider Order** — for [file preview providers](../actions-and-preview#quicklook-preview)
(for example, the [QuickLook Bridge](../actions-and-preview#external-preview-via-quicklook-optional)
plugin sets its own priority above every built-in previewer). The same up/down-arrow (or
drag-to-reorder) list used elsewhere in Settings (see [Favorites](./favorites)): move a provider up
to make it always win over the ones below it, regardless of its own built-in priority. Only
providers whose plugin component is currently enabled are listed here — one disabled under
[Plugins](./plugins) would never actually preview anything anyway.

**Thumbnail Provider Order** — same mechanic, for the icons/thumbnails shown next to results in the
search list itself (as opposed to the preview pane) — most relevant once more than one plugin
implements a custom thumbnail provider, since only one built-in provider exists today.
