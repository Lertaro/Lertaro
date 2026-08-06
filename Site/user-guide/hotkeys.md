# Hotkeys

All global and in-app hotkeys below can be rebound from **Settings → Hotkeys**; defaults are shown
here. See [Settings → Hotkeys page](./settings/hotkeys-page) for the settings UI itself.

## Global hotkeys

| Action | Default | Notes |
|---|---|---|
| Toggle quick window | Double-tap `Ctrl` | Can also be set to a full combo (e.g. `Alt+Space`) instead of a double-tap. |
| Quick switch | `Ctrl+G` | In a supported file dialog, navigates to the last folder captured from a supported file manager or the Quick Panel. An empty inline query also shows that destination as a result. |
| Select next item | `Ctrl+N` | Also works as the literal Down arrow. In the [Quick Panel](./settings/quick-panel) it walks the whole tab rather than one group, carrying on into the next group at the end of the current one. |
| Select previous item | `Ctrl+P` | Also works as the literal Up arrow. Same whole-tab walk in the Quick Panel. |
| Jump to result 1–9 | `Ctrl` + digit | The modifier is configurable; the digit is always 1–9. The quick window shows each visible result's shortcut as a small badge next to it, so you don't have to count rows. |
| Open actions menu | `Ctrl+O` | Also works as the literal Right arrow on a selected result. |
| Complete from selection | `Ctrl+Tab` | In the quick window, fills the search box with the selected result's name/path. |
| QuickLook preview | `Alt+P` | Toggles the preview pane for the selected result. The [Quick Panel](./settings/quick-panel) takes the same key, and docks the preview to whichever side of itself has room for it. |
| Previous keyword history | `Alt+Up` | Cycles backward through your recently typed queries. |
| Next keyword history | `Alt+Down` | Cycles forward through your recently typed queries. |
| Delete keyword history entry | `Ctrl+Delete` | |
| Open full window | *(none)* | Opens the full window directly, carrying over the current query — same effect as left-clicking the [Quick Window's own logo](#search-box-logo-icon) and choosing Show Main Window from the menu that opens, without that extra step. Not bound by default; set one from **Settings → Hotkeys**. |
| Keep window open | `Ctrl+T` | Stops the window hiding when focus moves elsewhere, so a query can be assembled out of text copied from other windows — hiding would otherwise clear the search box each time. Lasts for the current summon and ends with the next hide. In the quick window, middle-clicking the logo does the same thing and the logo brightens while it is on; pressing the summon hotkey while it is up but unfocused brings it back rather than hiding it. The [Quick Panel](./settings/quick-panel) takes the same key, with a pin button of its own as the visible marker. |
| Toggle quick panel | `Ctrl+F2` | Opens the [Quick Panel](./settings/quick-panel) docked into the bottom-right corner of whatever window is in front, or closes it if it is already up. Once it is open, holding the "jump to result 1–9" modifier and pressing 1–9 switches workspaces. |

## Search box logo icon

The small logo icon in the search box (left or right side, depending on the window) does something
different in each of the [three windows](./getting-started#the-three-windows):

- **Quick window** — left-click (no movement) opens the same menu the tray icon's right-click shows
  (Show Main Window, Toggle Hotkeys, Settings, About, Clean Exit, Exit), anchored at the cursor; that
  menu's Show Main Window item also carries over whatever query you currently have typed. Click-and-drag
  moves the window, same as dragging any other part of the search bar — hold **Ctrl** while dragging
  (either the bar or the logo, and toggling Ctrl mid-drag works too) to constrain movement to vertical
  only, useful for nudging the window up or down without shifting it sideways. Right-click resets the
  window to its default on-screen position (not size) — the same one it centers to on first launch. A
  hover tooltip spells out all three behaviors.

  The remembered position is relative to whichever monitor the window was last on, not an absolute
  screen coordinate — summon it again on a different monitor (or one with a different resolution or
  DPI scaling) and it reopens at the equivalent spot there instead of potentially landing off-screen
  or on the wrong display.
- **Inline window** — only clickable when the window is docked to a native Open/Save/Browse-for-folder
  dialog: left-click opens [quick navigation](#quick-navigation-mouse), same as the dedicated trigger
  below. Not clickable when docked to a plain Explorer window or the desktop, since there's nothing
  useful to navigate to in that case — no hover highlight or tooltip appears either, so it stays quiet
  rather than looking clickable and doing nothing.
- **Main window** — the logo is purely decorative there; clicking it does nothing.

## Quick navigation (mouse)

Enabled by default, toggled per-trigger in settings:

- **Double-click** empty space on the desktop or inside an Explorer window to trigger quick navigation.
- **Middle-click** empty space on the desktop or inside an Explorer window — or the file list of a
  supported third-party file manager (Directory Opus, Total Commander, XYplorer, Files, ...), or a
  native Open/Save/Browse-for-folder dialog — to trigger quick navigation. Those other windows only
  respond to middle-click: double-clicking there already means "open this," so double-click isn't
  repurposed. See [Supported File Managers](./file-manager-support) for what each integration covers.
- When the inline search window is docked to a native Open/Save/Browse-for-folder dialog, its own
  logo triggers quick navigation too — see [Search box logo icon](#search-box-logo-icon) above.

Any of these triggers pops a cascading menu of your Favorites, History, and configured quick-access
folders (see [Settings → Favorites](./settings/favorites) and [Settings → History](./settings/history))
— plugins can contribute their own entries too, such as Total Commander's own Directory Hotlist if
you've set one up in `wincmd.ini`, Directory Opus's own Favorites menu, or a [Custom
Command](./instant-answers#custom-commands) flagged
"Show in Quick Navigation" (optionally nested into a submenu by giving it a `/`-separated path). Each
contributing plugin gets its own labeled section at the root of the menu, and the order those
sections appear in is yours to set — see
[Settings → General → Quick Navigation](./settings/general#quick-navigation). Clicking a folder
navigates the target window there; clicking a file navigates there too, landing on the file selected
in its containing folder rather than opening it — the one exception is the desktop, which has no
existing window pane to navigate within, so there a folder or file is opened directly, same as
double-clicking it would. Inside a file dialog specifically, clicking a file instead jumps the
dialog to that file's folder — it deliberately never auto-confirms Open/Save on your behalf.

The **Folder Cascader** plugin is what actually builds this menu. Beyond Favorites and History (each
independently toggleable), it has its own configurable list of quick-access folders — from
**Settings → Plugins → Folder Cascader → Configure**, add a folder's path and an optional display
name, and give it a `Submenu` value (e.g. `Tools/Network`, `/`-separated for multiple levels) to nest
it under a category instead of showing it at the root. Every level of the menu — the root and any
nested category — also has a small **+** button on its own header: click it to add the folder you're
currently browsing right there, pre-filled with its name, path, and that level's own submenu path
(all still editable before confirming), without leaving the menu to open Settings.

## Hardcoded keys (not configurable)

These always behave the same way regardless of your hotkey settings:

| Key | Context | Behavior |
|---|---|---|
| `Escape` | Anywhere | Clears the search box if it has text; otherwise closes the window (or exits the actions menu). |
| `Enter` | Result list | Opens the selected result. |
| `Ctrl+Enter` | Result list | Locates the result in Explorer instead of opening it. |
| `Ctrl+Shift+Enter` | Result list | Opens the result elevated (Run as administrator). |
| `Left` / `Right` arrow | Actions menu | Go back a menu level / enter a submenu. |
| `Backspace` | Actions menu | Exits the actions menu when the search box is already empty. |
| `Alt+Space` / `Alt+F4` | All of Lertaro's own windows | `Alt+Space` is suppressed on all of them: none has a real title bar for the Windows system menu to belong to. `Alt+F4` closes the main and settings windows as usual; it stays suppressed on the quick, inline and QuickLook windows and the dialogs, which are shown and hidden rather than opened and closed. |

## Plugin action hotkeys

Plugins can register their own actions with a default hotkey (e.g. copy path (`Ctrl+Shift+C`), run
as admin, or the built-in file actions — Cut `Ctrl+X`, Copy `Ctrl+C`, Paste `Ctrl+V`, Delete
`Delete`, Permanently Delete `Shift+Delete`). These show up under **Settings → Hotkeys → Plugin
Actions**, grouped by the plugin that registered them, and can be rebound the same way as built-in
hotkeys.

## Process blacklist

If Lertaro's global hotkeys interfere with another application (a game capturing raw keyboard
input, for example), add that process to the **Process Blacklist** — see
[Settings → Hotkeys page](./settings/hotkeys-page#process-blacklist). While a blacklisted process is
in the foreground, Lertaro's global hotkeys, keystroke interception, and the quick navigation
mouse triggers above are all let through untouched.

Any foreground app that's genuinely full-screen gets this same treatment automatically — no
blacklist entry needed. Either way, an active file dialog is always exempt, so quick navigation
still works there.
