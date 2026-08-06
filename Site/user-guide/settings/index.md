# Settings Reference

The Settings window resizes and maximizes like any other: drag its edges, use the maximize button in
the title bar, or double-click the title bar. Worth doing on the Plugins page in particular, which
puts a plugin list and that plugin's settings side by side and has real use for the width.

A search box sits in the Settings window's title bar. It matches fuzzily (the same fzf-style
matching the main search window uses, with pinyin alias support), not just plain substrings, across
every section — including the per-plugin entries under Plugins and Hotkeys' Plugin Actions tab. Each
result shows a breadcrumb (e.g. "Index > Network Drives")
so same-named settings under different tabs stay distinguishable. Selecting a result (click, or
Up/Down to highlight and Enter) switches to the right section and tab, scrolls the exact control
into view, and briefly flashes a highlight border around it.

Several sections (General, Hotkeys, Index, History, Quick Panel, Service Status) further split into
their own row of sub-tabs at the top of the page. If the tab labels don't all fit — most often in
English, since translated labels usually run longer than their Chinese originals — left/right arrow
buttons appear at the ends of the row so the rest stay reachable by scrolling, instead of just being
cut off.

The Settings window has ten sections in its left sidebar:

| Section | Covers |
|---|---|
| [Service Status](./service-status) | Background service install, and the App/Hook/Service log viewer. |
| [Index](./index-drives) | Local drives, network drives, WSL distributions (once detected), folder indexes, and exclusion rules. |
| [General](./general) | Startup behavior, updates, language, search bar layout, and preview window size. |
| [Hotkeys](./hotkeys-page) | Global hotkeys, per-plugin action hotkeys, and the process blacklist. |
| [Plugins](./plugins) | Installed plugins and per-component enable/disable toggles. |
| [LocalSend](./localsend) | Cross-device LAN file, folder, and plain text transfer settings (compatible with official LocalSend clients). |
| [Favorites](./favorites) | Custom-named shortcuts to folders, files, and URLs. |
| [History](./history) | Search history and quick-window keyword history. |
| [Quick Panel](./quick-panel) | The floating panel docked over the window in front: its workspaces, their sources, the tabs plugins contribute, and which apps each workspace belongs to. |
| [Appearance](./appearance) | Theme picker (with a preview card per theme) and "follow system light/dark" mode. Pinned above About. |
| [About](./about) | Version info and update checking. |

Each page below documents every option on that section, in order, with its default value and any
valid range.
