# Quick Panel

While the panel is open, the folder shown in the selected group's heading is also treated as the current directory. It does not matter whether the selected tile is a file or a folder: when that heading has a path, open a supported file dialog and use Quick switch (`Ctrl+G` by default) to navigate there.

A floating panel summoned by a hotkey and docked into the bottom-right corner of whatever window is
in front, at half the height and half the width of it. It shows folders you nominate, and lists your
plugins contribute — as thumbnail tiles or as a list — so files can be reached, dragged out of, or
dropped into without leaving the window you are working in. Drag its top edge to move it somewhere
else for the current summon.

- **Enable the quick panel** — master switch; off means the hotkey does nothing.

The key that summons it is on the [Hotkeys](./hotkeys-page) page (`Ctrl+F2` by default).

The panel cannot be resized below 280 × 200 pixels, so its tabs, filter box, and content remain usable. When a Lertaro window is already in front, the summon hotkey is intentionally ignored instead of opening a panel over another Lertaro window; if the panel is already open, the same hotkey still closes it.

The page below that switch splits in two: **Workspaces**, the sets of folders you assemble yourself,
and **Plugin tabs**, the ones your plugins contribute. Both end up in the same tab strip.

## Workspaces

The panel shows one tab at a time, and a **workspace** is one of them. A workspace is a set of
sources assembled for one kind of work — a project's folders, a place you keep reference material,
an inbox you drop things into.

The left-hand list is the workspaces themselves, with **New workspace**, **Duplicate workspace** and
**Delete workspace** buttons, and the same up/down-arrow (or drag-to-reorder) list used elsewhere in
Settings (see [Favorites](./favorites)). Top to bottom here is left to right in the panel's tab strip.

- **Name** — what its tab is labelled. Left empty, it falls back to a translated default, so a
  never-renamed workspace follows the UI language.
- **Enabled** — the checkbox beside each workspace. Off keeps the workspace configured but gives it
  no tab, for one set up for work you are not doing this month, where deleting it would mean
  rebuilding the source list to get it back. The **×** on a tab in the live panel does exactly this,
  which is why turning it back on is done here.

The selected workspace is edited through two sub-tabs: **Sources** and **Apps**.

## Sources

Each source is one group in the panel, shown in the order this list is in. **Add folder** picks one;
the checkbox beside a row hides that group without removing it; the name box overrides the group's
heading (leave it empty for the folder's own name); and **More options** opens the rest:

- **Show** — what the group draws from the folder:
  - **Recently changed files** — only what changed recently, newest first, answered from the index
    rather than by walking the folder.
  - **Everything, newest first** — never hides a file on age, only decides what comes first.
  - **Everything, by name** — a folder used as a shortcut bar.
- **Folder** — the folder itself, with a **…** button to browse for it.
- **Include subfolders** — off by default.
- **Accept dropped files** — files and folders dragged onto this group are copied into its folder,
  using Windows' own file copy (its progress dialog, its conflict prompts, its undo). Always a copy,
  never a move. Off by default and asked per source: a folder kept as an inbox wants it, and one you
  only ever read from does not.
- **Filter** — optionally limit what this source shows with one or more patterns separated by `;` or `,`
  (e.g. `*.mp4;*.mkv`). Leave it blank, which is the default, to show everything.
- **At most** — how many entries the group shows. 0 means everything the source has.
- **Changed within (minutes)** — only entries changed within this long qualify. 0 means no age limit.
- **Show as list** — the group opens as a detail list rather than thumbnail tiles. Which suits a
  group is a property of the folder: images want tiles, documents want names and dates. Tiles divide
  the row between five, so a panel docked to a wide window gets thumbnails worth looking at rather than
  more of them — more than five once even five would be wider than a thumbnail can fill, fewer than
  five on a panel too narrow for that many legible ones. Whatever the count, the row is divided rather
  than filled with fixed-size tiles, so it never ends in a gap. Each picture keeps its own shape, so a
  folder of 16:9 video does not become a grid of tall boxes with a band of empty tile in each.

A folder added here starts on **Everything, by name** — a folder picked by hand is nearly always a
place things are kept. The workspace a fresh install comes with is the exception, and deliberately so:
Desktop, Downloads and Documents are places things arrive, and start on recently-changed files.

## Plugin tabs

Tabs contributed by plugins, listed on the page's own second tab rather than inside a workspace: what
a plugin offers is a whole collection, and it has no more to do with one workspace's folders than with
another's. Each is ticked into the strip or unticked out of it once, for the panel as a whole.

CoreExtensions ships five:

| Tab | What it lists |
|---|---|
| **Favorites** | Your [Favorites](./favorites), in the order you arranged them. Web addresses among them open in your browser. |
| **History** | What you opened through Lertaro itself, most recent first — the only one of these that includes applications. |
| **Windows Recent Items** | The shell's own recent-documents list, resolved to the files it points at, newest first. |
| **Last Directory** | Whatever folder you were last browsing, in Explorer or in any application's file dialog — so the panel can hand back the folder you just came from. |
| **Recent Files** | The newest files across the folders the panel is set up to watch, answered from the index rather than by walking them. Files only. |

- The checkbox puts the tab in the strip or takes it out. The **×** on a live tab is the same thing,
  which is why ticking it back on is done here.
- **Show as list** — the tab opens as a detail list rather than thumbnail tiles, the same choice a
  folder source has, asked here because a plugin tab has no row in a source list to ask it on. The
  panel's own header toggle still overrides it for as long as the panel is open.

Only tabs whose plugin component is enabled under [Plugins](./plugins) are listed, and that is a
different question: unticking here takes the tab out of the strip, disabling there stops the provider
running at all. A tab closed while its plugin was switched off stays closed when the plugin comes
back — the state is kept against the component, not rebuilt from whatever happens to be loaded.

A plugin tab holds one list and wears no heading — the tab is already named after it. It takes no
dropped files, and a plugin that returns nothing gets no tab at all.

## Apps

Applications this workspace belongs to, one process name per line (`chrome` or `chrome.exe`, either
way). Summon the panel over one of them and it opens on this workspace instead of wherever it was
left — the app you are already in says which set of folders you mean. Left empty, the workspace is
only ever reached by hand.

## Quick panel only

Applications the panel stays out of, one process name per line. Added on top of the global
[process blacklist](./hotkeys-page#process-blacklist) rather than replacing it: whatever is blocked
globally is blocked here too. This list is for apps only this panel has a reason to avoid — it docks
itself onto the window in front, so a full-screen player or a game is ruined by it without deserving
a global block.

## Using the panel

- **Filter box** — right of the tab strip, focused the moment the panel opens. It matches fuzzily
  (the same fzf-style matching the search window uses, pinyin aliases included) against the current
  workspace only. A group with nothing left matching is hidden while the filter stands.
- **Enter** opens whatever is selected, which is also what a double-click does. The first entry is
  selected from the start, so a summon can be answered with type-then-Enter without ever leaving the
  filter box.
- **Up/Down** move the selection, and cross from one group into the next rather than stopping at a
  group's last row — the groups read as one list top to bottom. They work from the filter box without
  taking the keyboard out of it, so narrowing and picking are the same gesture. Inside a group shown as
  tiles, they still move a row at a time as the grid is laid out. The configured "select next/previous
  item" hotkeys (`Ctrl+N`/`Ctrl+P` by default) do the same, unless a plugin action is bound to one of
  them, which wins.
- **Switching tabs** — hold the "jump to Nth result" modifier (`Ctrl` by default, see
  [Hotkeys](../hotkeys)) and press 1–9, or click a tab. Workspaces and plugin tabs share one strip and
  one order; tabs drag to reorder, and each has an **×** that takes it out of the strip (turning a
  workspace off, or closing a plugin tab — both undone in the settings above). Closing the last one
  closes the panel.
- **Group headings** carry a sort toggle (by name / by date modified), a view toggle (tiles / list), and a collapse arrow. Click the heading itself or its arrow to expand or collapse the group, like a standard Windows group header; the sort and view controls keep their own actions. What you do to a group here lasts for as long as the panel is open; the starting state is what the settings above say.
- **Line numbers** — every group shares the left gutter. List view numbers items; tile view numbers
  visual rows. Its width follows the largest visible number, so the gutter stays aligned. Scrolling
  never changes a group's expanded state; use its collapse arrow when you want to open or close it.
- **Selecting several** — in tile view, drag a box across empty space to rubber-band a set. A
  selection belongs to one group, since each group draws its own list; clicking empty space clears it.
- **Dropping files in** — drag files, folders, or an image straight off a web page onto a group that
  accepts drops. Anything a drag can offer as a file is taken, not just images. The group reloads
  itself once the copy finishes.
- **Preview** — the QuickLook key (`Alt+P` by default, rebindable on the [Hotkeys](./hotkeys-page) page
  like any other) opens the preview window for the selected file, and it follows the selection from
  then on. It docks to the right of the panel, or to its left when the screen edge leaves no room there
  — which is what a panel in the bottom-right corner of a maximized window gets. Clicking into the
  preview does not dismiss the
  panel; clicking away from both does. It closes with the panel.
- **Keeping it open** — the panel closes when it loses focus. The pin button, or the "keep window
  open" hotkey (`Ctrl+T` by default, the same one the quick window uses), suspends that for the
  current summon.
- **Escape** clears the filter box if it has text, and closes the panel otherwise.
- The panel keeps itself current: a folder it is showing that changes on disk is reloaded, through
  the same index-backed watching the rest of the app uses rather than a scan of its own.
