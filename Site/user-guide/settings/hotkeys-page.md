# Hotkeys (Settings Page)

Three tabs: **Global**, **Plugin Actions**, and **Process Blacklist**. See the
[Hotkeys](../hotkeys) page for what each shortcut actually does — this page documents the settings
UI itself.

## Global

**Global Hotkeys** group:

- **Show/Hide Quick Search** — recorder control; accepts either a bare modifier (double-tap mode,
  default `Ctrl`) or a full combo. Next to it, **Still respond while a fullscreen app is focused**
  — checkbox, off by default — opts this hotkey (and Quick Switch, and inline-search activation)
  out of the automatic fullscreen exemption described under [Process Blacklist](#process-blacklist)
  below.
- **Quick Switch** — default `Ctrl+G`.

**Function Keys** group:

- Next Item (`Ctrl+N`), Previous Item (`Ctrl+P`), Jump-to-Result modifier (default `Ctrl`, paired
  with digits 1–9), Open Actions Menu (`Ctrl+O`), Complete from Selection (`Ctrl+Tab`), QuickLook
  (`Alt+P`), Previous/Next Keyword History (`Alt+Up` / `Alt+Down`), Delete Keyword History Entry
  (`Ctrl+Delete`), Open Full Window (default `Ctrl+F` — opens the full window directly, carrying
  over the current query; same effect as left-clicking the Quick Window's own logo and choosing
  Show Main Window, without that extra click).
- Every recorder here accepts any key or combo you press — including keys like a bare `Tab` — and
  that binding takes priority over any hardcoded default meaning that key might otherwise have.

**Quick Navigation** group:

- **Double left-click in empty space** — checkbox, default on.
- **Middle-click in empty space** — checkbox, default on.

## Plugin Actions

One entry per action a plugin has registered (e.g. copy path, run as admin), grouped by plugin
name, each with its own hotkey recorder. Falls back to the plugin's own suggested default until you
change it.

## Process Blacklist {#process-blacklist}

Add executable names (e.g. `game.exe`) whose foreground focus should suppress Lertaro's global
hotkeys, keystroke interception, and the quick navigation double-click/middle-click mouse triggers
entirely. Case-insensitive, `.exe` suffix optional. Supports the same add-one / bulk-edit pattern as
the exclusion rules under **Index**: a single-entry textbox plus **Add Process**, a list of
existing entries, and a bulk textbox with **Generate Text** / **Apply Text**.

This is the fix for hotkey conflicts with fullscreen games or other apps that grab raw keyboard
input — see [Troubleshooting](../troubleshooting#the-global-hotkey-doesn-t-respond). Any foreground
app that's genuinely full-screen gets the same treatment automatically, with no entry needed here —
unless **Still respond while a fullscreen app is focused** (under **Global**, next to Show/Hide
Quick Search) is turned on, which opts back out of that exemption entirely. Either way, an active
file dialog is always exempt, so quick navigation still works there.
