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
  - **Usage Counts & Cleanup**: Each row shows how many times it has been opened. Choose a threshold and click **Clear with at most N uses** to remove low-use entries, or remove individual rows and click **Clear All History** to wipe everything.
  - **Live Updates**: The list refreshes its usage counts when another search window records a new launch.

## 2. Keyword History

Keyword History remembers the **raw query strings** that were actually used to execute a result or action, or to open the Full Window. Merely typing a query and dismissing or leaving the Quick Window does not add it:

- **Hotkey Navigation**: Inside the Quick Window, press **`Alt+Up`** / **`Alt+Down`** to cycle backward and forward through recent queries. Press **`Ctrl+Delete`** to delete the currently active term.
- **Usage Counts & Cleanup**: Each keyword shows how many times it has been used. Choose a threshold and click **Clear with at most N uses** to remove low-use keywords.
- **Independent Maintenance**: Includes its own **Enable History** toggle, search filter, single-entry deletion, and **Clear All History** button.
