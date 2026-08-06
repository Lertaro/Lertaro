# Example Plugins

Two plugins ship with Lertaro itself and are useful, real-world references — both live in the
`Plugins/` folder of the Lertaro repo.

## CoreExtensions — actions and the shell context menu

`CoreExtensionsPlugin` implements three interfaces at once: `IPlugin`, `IActionProvider`, and
`IConfigurable`.

- **`IActionProvider.GetActions()`** returns ten built-in `ISearchResultAction`s — open, locate in
  Explorer, copy path, copy/cut the file itself, open a command prompt at its location, touch/mkdir,
  and elevated (run-as-admin) variants of open and command-prompt.
- **`IActionProvider.GetDynamicActionProviders()`** returns a single `IDynamicActionProvider` —
  `ShellMenuActionProvider` — which is what makes the real Windows right-click menu (including
  nested cascade submenus like "Send to") appear inside Lertaro's own Actions menu. This is the
  pattern to copy if you want to surface *any* external, dynamically-built menu inside Lertaro
  rather than a fixed list of actions.
- **`IConfigurable.GetConfigSchema()`** demonstrates a config schema with nested field groups and a
  `StringList` field type — worth reading if your own plugin needs more than a flat list of
  booleans in its Settings → Plugins configuration dialog.
- Five providers implement
  [`IQuickPanelTabProvider`](./sdk/ui-extensions#iquickpaneltabprovider), and between them cover
  both ends of that interface. `FavoritesTabProvider` and `HistoryTabProvider` hand back an
  in-memory list as it stands — the minimal reference, since neither carries any state of its own.
  `WindowsRecentTabProvider` is the other end: it reads a directory and resolves shell shortcuts
  through COM on a background task, caps the set *before* doing the expensive part, and fills in
  each entry's `Metadata.Modified` so the tab's newest-first order means something.
- `LastDirectoryTabProvider` and `RecentFilesTabProvider` are worth reading for a different reason:
  neither has any data of its own at all. They ask the host for it through
  [`ExplorerPathService`](./sdk/services) and `RecentFilesService`, which is the pattern to copy
  whenever what your plugin wants to show is something Lertaro already knows.

## PinyinAlias — pinyin aliasing for Chinese filenames

`PinyinAliasProvider` implements both `IAliasProvider` and `ITranslationProvider` — a plugin can
freely combine SDK roles when they're related, and this one is a good template for that:

- **`IAliasProvider.InputRanges`/`OutputRanges`** declare its two alphabets straight from
  `PinyinEngine`'s own table bounds (`InputRanges`: the CJK block; `OutputRanges`: `a`-`z`) instead of
  duplicating magic numbers — the host uses these to support mixed literal+pinyin queries like `大cj`
  against `大长今`.
- **`IAliasProvider.CanHandle(text)`** scans for any Chinese character before doing any real work,
  so non-Chinese filenames skip alias generation entirely.
- **`IAliasProvider.GetAliases(text)`** builds a per-character syllable table (each Chinese
  character maps to its possible pinyin readings), then yields both a full-pinyin alias and an
  initials-only alias. For filenames with polyphonic characters (more than one valid reading), it
  generates aliases for every common combination — capped at 32 combinations to avoid a
  combinatorial blowup on pathological inputs — joining alternatives with `|` so the search engine
  treats each as a candidate rather than requiring all of them to match simultaneously.
- **`ITranslationProvider`** is implemented on the *same* class, purely to supply this plugin's own
  UI strings (e.g. its display name) via `TranslationService.LoadEmbeddedTranslations` — the two
  interfaces are unrelated in purpose but happen to live on one type here since it's a small,
  single-file plugin.
- A `Dictionary<string, Dictionary<string, string>>` cache guarded by a `lock` avoids re-parsing
  the embedded translation JSON on every call — the standard pattern for any plugin doing
  non-trivial work in `GetTranslations`.

Reading both plugins side by side is the fastest way to see how the pieces in the
[Plugin SDK Reference](./sdk/core-search-actions) fit together in practice.
