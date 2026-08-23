# Instant Answers & Keyword Shortcuts

Some results appear instantly as you type, without waiting for a file search — either always on,
or gated behind a short keyword you type before your actual query (so a large feature like browser
history search doesn't compete for attention on every unrelated keystroke). Most keyword-gated
ones have their keyword configured in **Settings → Plugins → Configure**.

## Always on

These need no keyword — they activate as soon as what you type matches their pattern.

### Calculator

Type a math expression directly and the result appears live, with **Enter** copying it to the
clipboard:

```
12 * (4 + 3)
```

Explicit base conversion is also supported:

```
255 to hex
0xFF to dec
```

### Environment variables

- `%NAME%` expands a specific variable (including multi-path variables like `%PATH%`, split into
  one entry per path).
- `%partial` fuzzy-lists every variable whose name matches `partial`.

### Run a command

- `#<command>` opens a Command Prompt window and runs `<command>` **as Administrator**.
- `$<command>` opens a Command Prompt window and runs `<command>` normally.

### Bare URLs

Type or paste a `http://`/`https://` address and Lertaro offers to open it directly.

## Keyword-triggered (built-in, configurable prefix)

Type the keyword, a space, then your query. Each keyword defaults to a short prefix but can be
changed independently in that plugin's own **Configure** dialog if it collides with something you
type often.

| Keyword (default) | Plugin | What it searches |
|---|---|---|
| `ps` | Process Manager | Running processes by name, PID, or window title (fuzzy/pinyin matches too) — select one to kill it. |
| `win` | Window Switcher | Currently open, switchable windows — the same set Alt+Tab shows — by title, process name, or PID. Select one to bring it to the foreground. |
| `bm` | Browser Data | Bookmarks and history from Chrome/Chromium-family and Firefox-family browser profiles you've added under that plugin's own settings. |
| `set` | Core Extensions | Every setting in the app, fuzzy-matched by name — pick one to jump straight to it in Settings, highlighted. Type `set` with nothing after it to list every setting. |
| `flow` | Flow Launcher Bridge | Lists all loaded Flow.Launcher plugins and their action keywords — select one and press Enter to open its settings window. |

Window Switcher shows a live thumbnail of each window's actual content as its icon by default,
captured in the background so it never slows down typing — a plain app icon shows immediately, and
the thumbnail fades in once it's ready. Turn off **Show Window Content as Icon** in
**Settings → Plugins → Window Switcher → Configure** to skip that capture entirely and always use
the app icon. A window running as true exclusive fullscreen (most games default to borderless
windowed instead, which this handles fine) can't be captured this way and always falls back to its
app icon.

Browser Data indexes bookmarks and history independently — **Index Bookmarks** and **Index History**
are separate toggles in **Settings → Plugins → Browser Data → Configure**, so you can turn history
off (it tends to grow far larger than bookmarks) while still searching bookmarks, or vice versa.

## Web search

Web Search ships with default keywords for several engines/sites — `bd` (Baidu), `g` (Google),
`bing` (Bing), `gh` (GitHub), `wiki` (Wikipedia), `yt` (YouTube) — and lets you add, edit, or
remove entries entirely, each with its own name, keyword, icon, and URL template, from
**Settings → Plugins → Web Search → Configure**.

```
g lertaro github
```

opens a Google search for "lertaro github" in your browser.

## Translation

Translator's default keyword is `tr`. Type text after it to translate into the language currently
selected for Lertaro's interface:

```
tr hello
```

The result appears asynchronously; press **Enter** or click it to copy it. The text to translate is
sent to Microsoft Translator, so this feature requires an Internet connection. Its target language
is the App's current language setting. Change the keyword under **Settings → Plugins → Translator →
Configure**.

## File Filters

Index specific folders under their own rule, configured entirely under **Settings → Plugins →
File Filters → Configure**. Each rule has its own **Target Folders** list (scanned recursively),
an **Extensions / Pattern** field (e.g. `*.exe;*.lnk` — several patterns separated by `;` or `,`
are all matched; the default `*` matches every file), and an optional **Filter Name** shown in the
result description. Subfolders are always included regardless of the pattern — only files are
filtered by it.

Add a **Shortcut Keyword** to gate a rule's matches behind a prefix, the same as the built-in
keywords above, instead of always mixing them into the general index.

## Fully custom (you define the keyword)

### Custom Commands

Define your own `<keyword> <args>` commands that launch an external program, configured entirely
under **Settings → Plugins → Custom Commands → Configure**. The command's parameter template
supports positional placeholders (`%s1`, `%s2`, ... for each space-separated argument) and a
whole-remainder placeholder (`%s` for everything typed after the keyword).

Each command also has a **Show in Quick Navigation** checkbox (off by default) — turn it on to also
list that command as its own entry at the root of the [Quick Navigation](./hotkeys#quick-navigation-mouse)
menu, clickable without typing its keyword at all. The **Quick Navigation Submenu** field nests it
under a named submenu instead of the root — use `/` to nest multiple levels deep (e.g. `Tools/Network`
for a two-level submenu); leave it empty to keep the command at the top level. A command shown this
way runs with its configured parameters as-is, since there's no typed argument text to substitute
into `%s1`/`%s` here.

---

None of the plugins on this page are required — each can be disabled independently under
[Settings → Plugins](./settings/plugins) if you don't want it competing for keyboard space, and
[Search Syntax](./search-syntax) covers the separate, always-active fuzzy query language used for
everything else (files, folders, applications).
