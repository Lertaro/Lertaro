# History

The three tabs manage opened items, search terms, and learned query associations separately.

## 1. Search History

Records how often and recently each file, folder, or application was opened from search results.

- **Ranking:** each open adds 1 to a score capped at 10. Scores decay with a 90-day half-life. A score of 3 qualifies for H3 and 5 for H5. Quick Search has up to 3 H5 and 10 H3 slots; inline search has 1 and 3. The Full Window has no slot cap.
- **Scope:** usage can move an item ahead of other results with the same effective match grade and priority. Quick Search's type settings can lower exact file/folder matches to the strong-match grade; see [Search Syntax](../search-syntax).
- **Inline groups:** current-folder and global results are grouped separately. Missing paths are skipped.
- **Management:** filter by keyword, delete individual entries, clear entries with at most N uses, or clear all. Lists refresh when another window records a launch. Disabling history prevents new entries; existing entries still update their counts and recency.

## 2. Keyword History

Stores the original query when you execute a result or action, or open the Full Window. Typing and dismissing Quick Search alone does not create a record.

- Press **Alt+Up / Alt+Down** to browse previous queries and **Ctrl+Delete** to remove the current entry.
- Filter, delete, clear all, or remove keywords with at most N uses. This tab has its own recording switch. When disabled, existing keywords still accumulate uses, but new keywords are not added.

## 3. Query Associations

Stores **query → target path** associations independently of general usage scores.

- An association becomes eligible after the same normalized query is used to open a normally found result at least **3 times**. An open from learned recall alone does not increase this count.
- At most **one** association supplements a query. Low-priority targets are excluded, and association-only results follow direct matches.
- Filter, delete, or clear associations without resetting path usage scores.
