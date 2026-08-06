# Actions Menu & Preview

## Actions menu

Every result — file, folder, or app — has a set of actions beyond "just open it": locate in
Explorer, copy path, run as administrator, cut/copy/paste the file itself, delete it (to the
Recycle Bin) or permanently delete it, and anything a plugin adds (for example, the full Windows
shell right-click menu, cascading submenus included).

Open it with the **Open actions menu** hotkey (`Ctrl+O` by default) or the literal Right arrow on a
selected result. Inside the menu:

- **Next/Previous item** — the arrow keys, or your own configured
  [next/previous-item hotkey](./hotkeys), move the highlight up and down the actions list. A
  custom binding (even something like a bare `Tab` key) is honored here exactly the same way it is
  in the main result list.
- **Right arrow / Enter** on an item with a submenu (e.g. a shell cascade menu like "Send to")
  drills into it; **Left arrow** or **Backspace** (with an empty search box) backs out one level.
- **Escape** exits the actions menu, or clears the search box first if you'd typed something to
  filter the action list.
- Type to filter the visible actions by name, the same way you'd filter search results.

## Full window results grid

Double-clicking a result normally opens it, same as pressing Enter — with one exception: double-
clicking the **Path** column instead opens the result's containing folder in Explorer, the same
thing `Ctrl`+Enter does anywhere else on the row. A plugin's own custom column can define this same
kind of double-click override for itself.

The grid holds every match rather than a capped page of them, and fills in as the results arrive
instead of appearing all at once when the search finishes — a broad query on a large drive can run
for several seconds, and the rows are usable throughout. Arrow-key navigation wraps at both ends:
pressing Up on the first row moves to the last, and Down on the last row moves back to the first.

The window has no title bar, so its header is the drag area: press anywhere in it that isn't the
search box or a window button and drag to move the window. While the pointer is over the header a
grip fades in across the top of it, marking where to grab.

## QuickLook preview

Press the **QuickLook** hotkey (`Alt+P` by default) on a selected result to open a preview pane
docked next to the search window — images, documents, and other previewable file types render
without leaving Lertaro. Press it again (or move to a result QuickLook can't preview) to close it.

Audio and video files — whatever formats WPF's built-in media playback supports without extra
codecs (MP4, WMV, AVI, MOV, MP3, WAV, WMA, and a few others) — play automatically as soon as the
preview opens, with a small themed transport bar (play/pause, seek, current/total time, mute)
instead of a static thumbnail. Moving to a different result or closing the preview stops playback
immediately. A file whose specific codec can't be decoded falls back to a static thumbnail instead.

The preview window's size is fixed and user-configurable — see
[Settings → General → Preview](./settings/general#preview) — and independent of how many results
are currently showing. Whatever size you set, Lertaro automatically keeps the preview window
fully on-screen: if it doesn't fit beside the search window on your monitor, it docks to whichever
side has room, and if the configured size is larger than your monitor's usable area, the window is
scaled down to fit rather than running off the edge.

If the file being previewed needs its own native handler to show a popup of its own — most
commonly Word or Excel asking for a password on an encrypted document — both the quick window and
the preview pane hide themselves for as long as that popup is open, since it would otherwise sit
unreachable behind them. This isn't Lertaro closing or crashing: resolve the popup (enter the
password, dismiss it, whatever it's asking) and both windows come back exactly as you left them,
search text and selection included.

The preview window's header is itself a drag source for the file being previewed — drag it into
Explorer, an editor, or any other drop target and it behaves exactly like dragging the result row
out of the search window.

Opening the full window from the quick one carries the preview's open state across, so a preview you
already had open stays open.

### External preview via QuickLook (optional)

This is a different thing from the built-in preview pane above, despite the shared name: a
separate, third-party application also called **QuickLook** ([QL-Win/QuickLook on
GitHub](https://github.com/QL-Win/QuickLook), GPL-licensed) that you install yourself — Lertaro
doesn't bundle it.

If it's installed and running, the bundled (experimental) **QuickLook Bridge** plugin — visible
and toggleable like any other plugin under [Settings → Plugins](./settings/plugins) — reaches it
over its own named pipe and takes over previewing for everything, ahead of every built-in preview
type described above. Lertaro's own preview pane hides itself while this is active, and
QuickLook's own floating window is moved into the exact spot that pane would have occupied instead
— visually it reads as "the preview pane became QuickLook," though technically QuickLook's window
stays a completely separate top-level window that Lertaro repositions to follow along.

An action menu entry, **Preview in QuickLook**, lets you send a result to QuickLook manually even
when some other preview type would otherwise win for it.

Since this depends entirely on QuickLook's own (undocumented, private) inter-process protocol
rather than any published integration API, a future QuickLook release could change that protocol
and silently break this. Uninstalling or closing QuickLook has no effect on anything else — Lertaro
simply falls back to its own built-in preview types.
