# History

Two tabs, each independent: **Search History** and **Keyword History**.

## Search History

Tracks results you've actually opened, so they can be prioritized the next time you search for
something similar.

Search History remembers both the opened path and the query that led to it. A later fuzzy match against that recorded query can recall and prioritize the existing path in the quick, inline, and full search windows—for example, opening `BCompare.exe` with `bcomp` lets `bc` bring it back near the top. Inline search keeps matches inside the active folder under **Current Folder** and places the rest under **Global Search**; missing paths are ignored and duplicate paths appear only once.

- **Enable History** checkbox — turns tracking on/off; existing entries are kept even if you
  disable it, they're just no longer added to.
- **Search box** — filters the visible history list by keyword.
- Each entry can be deleted individually, or all at once with **Clear All History**.

## Keyword History

Tracks the raw text you've typed into the quick window's search box (not what you opened), so you
can cycle back through recent queries with the
[keyword history hotkeys](../hotkeys#global-hotkeys) (`Alt+Up` / `Alt+Down` by default).

- Same **Enable History** toggle, search filter, per-entry delete, and **Clear All History**
  button, scoped to keywords instead of opened results.
