# Supported File Managers

Lertaro doesn't only search — it can also integrate with the file manager or dialog you're
currently using. Depending on the target, that integration can mean up to three things:

- **Inline search docking** — a Lertaro search bar embeds directly into the target window (the
  [inline window](./getting-started#the-three-windows)), so you can search without leaving it.
- **Quick Navigation** — the target responds to the [quick navigation mouse
  triggers](./hotkeys#quick-navigation-mouse) (double-click/middle-click, or the inline window's
  own logo), popping a cascading menu of Favorites, History, and quick-access folders.
- **Active path detection** — Lertaro can tell which folder is currently open in the target, so
  it can scope a search to that folder and resolve path-relative actions (like Copy Path) against
  it.

Not every integration provides all three — see the table below.

## Open folders in Quick Navigation

With **Show opened folders** enabled in the Folder Cascader plugin (on by default), Quick Navigation adds an **Opened folders** group above Favorites and History. It combines the filesystem folders currently open in every supported file manager, then removes duplicate paths. Virtual locations and folders that no longer exist are omitted, so every listed entry remains a valid navigation target.

## Built in (no extra install)

These come bundled with Lertaro's core extensions plugin — nothing to enable separately.

| Target | Inline search docking | Quick Navigation | Active path detection |
|---|---|---|---|
| Windows File Explorer | Yes | Double-click or middle-click | Yes |
| Modern Open/Save file dialog | Yes (this *is* the inline window) | Middle-click, or left-click the inline window's own logo | — |
| Classic Open/Save file dialog | Yes | Middle-click, or left-click the inline window's own logo | — |
| Classic Browse-for-Folder dialog | Yes | Middle-click, or left-click the inline window's own logo | — |

Active path detection doesn't apply to the dialogs themselves — there's no "other window" for
Lertaro to scope a search against once it's already docked inside the dialog.

## Optional plugins (Settings → Plugins)

Each of these ships as its own plugin. Install/enable it from [Settings →
Plugins](./settings/plugins), then use that plugin's own **Configure** dialog to toggle **Enable
Inline Search** and **Enable Quick Navigation** independently — both default to on.

| Target | Inline search docking | Quick Navigation | Active path detection |
|---|---|---|---|
| Directory Opus | Yes | Middle-click on a file list pane | Yes |
| Files | Yes | Middle-click on a file list pane | Yes |
| One Commander | Yes | Middle-click on a file list pane | Yes |
| Total Commander | Yes | Middle-click on a file list pane | Yes |
| XYplorer | Yes | Middle-click on a file list pane | Yes |

All five detect their target by matching the running process (and, for Directory Opus and Total
Commander, by talking to their documented remote-control interface over `WM_COPYDATA` rather than
scraping the UI); Files and One Commander instead use UI Automation, since neither exposes a
remote-control protocol.

## Directory Opus indexed folder sizes

When the Directory Opus plugin's **Enable Lertaro Size Column** option is enabled (the default), Lertaro installs a **Lertaro Size** script column in Directory Opus. It displays each indexed folder's recursive size from Lertaro's current in-memory index; it does not scan your disks. File rows continue to use Directory Opus's native size column. A folder that Lertaro does not index has no value in this column. Disabling the option stops Lertaro from writing or updating the script; it intentionally leaves an already installed script in place.

Add **Lertaro Size** in Directory Opus's column chooser. To keep it visible when changing folders and after restarting Directory Opus, use **Folder → Folder Format → Save → Save format for all folders**. A more specific Folder Format, such as one for local drives or network drives, takes precedence over the user default; add the column to that format as well when needed.

The generated size script works with Directory Opus 11 and 12 as well as 13 and later; older versions use a hidden compatibility runner and remove its temporary output immediately after each query.

## Everything IPC emulation

When **Enable Everything IPC emulation** is turned on in [Settings → General → System](./settings/general#system), Lertaro emulates the standard Everything Win32 IPC server interface. This allows Directory Opus, Total Commander, and other third-party tools to use Lertaro directly as their native Everything backend for instant global search and zero-I/O folder size calculation without requiring Everything to be running.

## Application dialogs (optional plugins)

These target one specific dialog inside a third-party application — not the whole application —
the same way the built-in dialogs above do. Install/enable from [Settings →
Plugins](./settings/plugins); each ships a single component with its own on/off switch there (no
separate Configure dialog, since there's only one thing to toggle).

| Target | Inline search docking | Quick Navigation | Active path detection |
|---|---|---|---|
| WPS Office's Open/Save dialog | Yes | Middle-click, or left-click the inline window's own logo | — |
| WinRAR's Extract dialog | Yes | Middle-click, or left-click the inline window's own logo | — |
| Bandizip's Extract dialog | Yes | Middle-click, or left-click the inline window's own logo | — |
| Bandizip's Add Files dialog | Yes | Middle-click, or left-click the inline window's own logo | — |
| AutoCAD's Open/Save dialog | Yes | Middle-click, or left-click the inline window's own logo | — |

Detected by control structure, not window title, so this works across every language pack each
application ships. The WPS entry covers Writer, Spreadsheets, Presentation and the PDF reader, which
all share the one dialog — WPS uses its own rather than the Windows one, which is why it needs a
plugin at all where most applications are covered by the built-in dialogs above. The AutoCAD entry
covers its native Open and Save dialogs, which use the standard Windows common-dialog class but a
distinct control layout. As with those, active path detection doesn't apply — Lertaro is already
docked inside the dialog itself, with no other window to scope a search against.

---

Building your own integration for a file manager not listed here? See the [System & Dialog
Adapters](../dev-guide/sdk/system-adapters) reference in the Developer Manual.
