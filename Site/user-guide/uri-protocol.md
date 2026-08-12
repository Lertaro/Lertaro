# URI Protocol (lertaro://)

Lertaro registers itself as the handler for a `lertaro://` link — no separate installer step,
it's set up automatically the first time the app runs. This lets anything that can open a link
(a browser, a shortcut, another app, a script) jump straight into a specific part of Lertaro,
instead of only being reachable through a hotkey.

If Lertaro isn't already running, opening a `lertaro://` link starts it and then follows the
link. If it's already running, the running instance handles the link directly — it never starts a
second copy.

## Routes

| Link | What it does |
|---|---|
| `lertaro://` | Activates the quick search window — same as summoning it with its hotkey. |
| `lertaro://search/[keyword]` | Activates the quick search window with `[keyword]` pre-filled. |
| `lertaro://fullsearch/[keyword]` | Opens the full search window with `[keyword]` pre-filled. |
| `lertaro://settings/page/[section]` | Opens Settings to a specific top-level section. |
| `lertaro://settings/entry/[index]` | Opens Settings and jumps straight to one specific setting, highlighted. |
| `lertaro://localsend` | Opens an empty LocalSend send window. |
| `lertaro://localsend/items[/encoded-item...]` | Switches to file/folder mode and optionally adds one encoded path segment per item. |
| `lertaro://localsend/text[/encoded-text]` | Switches to text mode and optionally fills in encoded text. |

```
lertaro://search/report
lertaro://settings/page/Appearance
```

The first activates the quick search window already filtered to "report"; the second opens
Settings directly on the Appearance page.

`[section]` matches one of the top-level sidebar entries: `Service`, `Index`, `General`,
`Appearance`, `Hotkeys`, `Plugins`, `Favorites`, `History`, `QuickPanel`, `About` — not
case-sensitive.

`[index]` isn't meant to be typed by hand — it's a number [Settings Search](./instant-answers)
generates itself for whatever setting you picked, so selecting one of its results round-trips
straight back to that exact row. It isn't stable across restarts, so don't rely on a specific
number staying the same.

## LocalSend links

Every file/folder path or text value must be URL-encoded as one complete path segment. To add multiple items, append one encoded segment per item; all paths must be absolute and must already exist. For example:

```
lertaro://localsend/items/C%3A%5CUsers%5Ctestuser%5CDesktop%5Ca.txt/D%3A%5CShared
lertaro://localsend/text/Hello%20world
```

`lertaro://localsend/items` opens the collection page in file/folder mode, while `lertaro://localsend/text` opens it in text mode. A link containing content proceeds to device selection, but never selects a device or starts a transfer automatically. If a send window is already open, the link does nothing and never changes its current content or state. If LocalSend is disabled, Lertaro opens its LocalSend settings page instead. Invalid or oversized content is ignored as a whole.

## Unrecognized links

Anything that doesn't match a known route — a typo, an unsupported section, garbage after
`lertaro://` — is silently ignored. Since any website or app can invoke this protocol without
asking you first, a bad or unexpected link should never do anything surprising; it's logged for
your own troubleshooting, but nothing else happens.
