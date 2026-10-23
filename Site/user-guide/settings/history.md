# History

The History settings page manages usage frequency, typed search terms, and confirmed query-to-target associations across three independent tabs: **Search History**, **Keyword History**, and **Query Associations**.

## 1. Search History

Search History tracks how often and recently you open each file, folder, or application so direct matches break ties intelligently:

- **Decayed Frecency Tie-Breaking**: Frequently opened items refine order **within the same match grade** using a 90-day half-life decay, without letting stale opens leapfrog an exact direct title or filename match.
- **Inline Window Grouping**: Inside file dialogs, matching items within the active folder appear under "Current Folder", while others appear under "Global Search".
- **Dead Link Filtering**: If an indexed path is moved or deleted, Lertaro automatically skips missing entries, guaranteeing unique rows.
- **Management Controls**:
  - **Enable History**: Master toggle; existing entries are retained when disabled. New entries are not added, while reopening an existing entry refreshes its recency.
  - **Search Filter**: Narrows the visible history list by keyword.
  - **Usage Counts & Cleanup**: Each row shows how many times it has been opened. Choose a threshold and click **Clear with at most N uses** to remove low-use entries, or remove individual rows and click **Clear All History** to wipe everything.
  - **Live Updates**: The list refreshes automatically when another search window records a new launch.

## 2. Keyword History

Keyword History remembers the **raw query strings** that were actually used to execute a result or action, or to open the Full Window. Merely typing a query and dismissing or leaving the Quick Window does not add it:

- **Hotkey Navigation**: Inside the Quick Window, press **`Alt+Up`** / **`Alt+Down`** to cycle backward and forward through recent queries. Press **`Ctrl+Delete`** to delete the currently active term.
- **Usage Counts & Cleanup**: Each keyword shows how many times it has been used. Choose a threshold and click **Clear with at most N uses** to remove low-use keywords.
- **Independent Maintenance**: Includes its own **Enable History** toggle, search filter, single-entry deletion, and **Clear All History** button. When disabled, existing keywords still accumulate usage counts, while new keywords are not added.

## 3. Query Associations

Query Associations stores confirmed **query → target path** habits separately from general path frequency:

- **3-Open Confirmation Threshold**: An item is recalled for a non-matching query only after you organically open it at least **3 times** for that normalized query, preventing one-off misclicks from polluting future searches.
- **No Feedback Loops & Monotonic Placement**: Launching an item from learned recall alone never self-reinforces unconfirmed counts, excludes `Low`-priority cache paths, and always places learned recall after direct matches.
- **Independent Cleanup**: Filter, delete individual associations, or clear all learned associations at any time without resetting your path usage counts.
