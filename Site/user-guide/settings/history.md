# History

The History settings page manages usage traces and adaptive recall rankings. Top tabs include: **Search History** and **Keyword History**.

## 1. Search History

Search History tracks items and applications you have actually launched, binding the search query to the target physical path:

- **Adaptive Prioritized Recall**: When you type characters related to past queries, Lertaro prioritizes previously opened items at the top of results. For instance, if you launched `BCompare.exe` by searching `bcomp`, future searches for `bc` will still boost it to the top.
- **Inline Window Grouping**: Inside file dialogs, matching historical items within the active folder appear under "Current Folder", while others appear under "Global Search".
- **Dead Link Filtering**: If an indexed path is moved or deleted, Lertaro automatically skips missing entries, guaranteeing unique rows.
- **Management Controls**:
  - **Enable History**: Master toggle; existing entries are retained when disabled, but new launches are not recorded.
  - **Search Filter**: Narrows the visible history list by keyword.
  - **Single Delete & Clear All**: Remove individual rows or click **Clear All History** to wipe everything.

## 2. Keyword History

Keyword History specifically remembers the **raw query strings** typed into the Quick Window:

- **Hotkey Navigation**: Inside the Quick Window, press **`Alt+Up`** / **`Alt+Down`** to cycle backward and forward through recent queries. Press **`Ctrl+Delete`** to delete the currently active term.
- **Independent Maintenance**: Includes its own **Enable History** toggle, search filter, single-entry deletion, and **Clear All History** button.
