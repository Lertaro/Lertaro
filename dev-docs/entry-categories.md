# Entry Categories (类型 / 目录分类)

One table answers "what kind of entry is this". `PluginSdk/Helpers/SearchCategoryClassifier.cs` holds the enum
(`SearchCategoryFilter`), the extension sets, `Classify` (name or path), `Categorize` (an `ISearchResult`, which
can also answer which provider contributed a row and where a shortcut points), `Matches` (the engine's per-row
test) and `RuleOf` (the same category as `*.doc; *.pdf; ...` glob text).

It lives in PluginSdk because that is the only assembly every consumer can reach. `Core` and
`Plugins/CoreExtensions` both reference `PluginSdk` and neither references the other; `App` references both.

## Who reads it

| Surface | What it takes from the table |
| :--- | :--- |
| Engine row filter (`NameSearch.CategoryAdmits`) | `Matches`, per row, while the index is walked |
| Core Extensions `:@keyword` rules | `RuleOf` -- the default rule text is generated, never hand-copied |
| Full window's sidebar type filters (`TypeFilterProvider`) | `Categorize` per row, and `Category` on each row |
| Quick window's filter bar | the same sidebar rows: it reads `ISidebarFilterProvider`, so it is the settings, not a list |

## One filter list, two triggers (2026-10-27)

`CustomFilters` is the single user-editable category list: `Enabled` / `Keyword` / `Label` / `Rule` / `ShowAsRow`
/ `Icon`. Typing `:@keyword` reaches every enabled entry; `ShowAsRow` also makes it a clickable row, which puts it
in the full window's sidebar and, through the provider above, in the quick window's filter bar. There is no second
list: `SidebarCustomFilters` was the same model with the same rule language, and the split let one category be
defined twice with two different rules (the row winning for clicks, the keyword for typing) while its trigger word
doubled as an untranslated display name.

`CustomFilterQueryTokenProvider.MergeSidebarRowsIntoFiltersOnce()` folds the old list on read -- from
`GetConfiguredFilters()` and from `SearchFiltersConfigSchema.Create()`, so neither the search path nor the settings
page can show the pre-merge shape. Deliberately unlatched: the steady-state cost is one dictionary miss, and a
latch would make the merge depend on which caller read first. An old row whose keyword already exists switches that
entry on instead of appending a duplicate; anything else is appended keeping its own rule, its icon, and its old
name as `Label`. The legacy key is then removed, so the merge is idempotent.

`SidebarFilterItem.Category` is what lets the **engine** narrow the index walk: the built-in rows carry one, a
custom `@` rule row leaves it null and filters per row only. Don't re-derive a category from a row's `Id` string.

Before the merge there were five lists to keep in step by hand -- Core's wire copy, the app's copy, the
`:@` defaults, their verbatim schema twin, and the sidebar's three shorter sets -- and two of them had already
drifted inside a single change (one video set carried `.mts`/`.ogv`, its twin did not). If a category is added or an extension moves, change the table here.

## Decisions that look like bugs

- **A shortcut passes every engine filter and classifies as `null`.** The side that can resolve a target (App,
  per row, on paint) is not the side that narrows the index walk (Service, which cannot resolve). Filing a
  shortcut by its target is therefore the caller's job; admitting it costs budget, never a result. Excluding it
  would drop every Start Menu row from the 应用 filter.
- **The engine only ever sees `delta.NameOf(row)`** -- a bare name, because rebuilding a row's path means walking
  its parent chain once per candidate over a 200k+ row scan. No path-shaped test may be added to that branch.
  Settings is recognised by name (`SystemSettings.exe`, `immersivecontrolpanel`, `ms-settings:`, and the host's
  `__SEARCHABLE_ITEM__:{Provider}:…` placeholder), and the substring walk is gated to the shapes that can
  contradict their own extension.
- **Path-mode queries carry neither the category nor the file-name filter** -- `IndexV2Searcher.SearchStreaming`
  hands those to `PathSearch` before `NameSearch` is reached. Pre-existing convention, not an oversight.
- **Network drives and the live-scan fallback are not narrowed at the source** (`ponytail:` note on
  `SearchService.SearchStreamingAsync`). The row filter in App still keeps the displayed list pure, so this costs
  budget only.
- **`.ts` means video, not TypeScript.** Inherited from the user's own `:@video` rule, which already said `*.ts`.
- **Application rows are filed by their path first, their own claim second.** The app indexer marks a shortcut's
  row as an application, so a shortcut to a document would otherwise sit under 应用 because the shell can launch it.

## Wire protocol

`SearchRequestMessage.Category` is a byte; `SearchRequestBinarySerializer` is at **v12** and both sides compare
`version != Version` and throw, so an App/Service pair from different builds fails *every* search (it degrades
through `onLocalSearchFailed`, it does not crash). The repo's stated rule -- a payload change requires a version
bump -- is why there is no tolerant read; the field defaults to `All`, so a tolerant read accepting v11 remains an
open option.

The payload size for `Search`/`SearchDir` ends in `+3` for three flag bytes. Note the trap: the frame length is
recomputed from the write offset, so a short formula does not corrupt the wire, it under-rents the buffer --
and each string is sized `GetByteCount + 5` while its 7-bit prefix costs 1–5 bytes, which over-provisions by at
least 5 and hides the error from every round-trip test. It became reachable only when the third flag byte was
added without the `+1`.

## The quick window's filter bar

Tabs are `[全部] + the plugin's Type rows`, read through `PluginManager.Instance.SidebarFilterProviders`, rebuilt
on every activation and after a settings save, and matched by `Id` so a refresh cannot bounce the user off their
tab. `SidebarFilterItem.Category` is what lets the engine narrow the walk; a custom `:@` row leaves it null and is
filtered per row instead. `SearchExecutionViewModel.ActiveCategoryFilter` is deliberately `null` for 全部 -- a
predicate that keeps every row is not "no filter", and passing one would silently disable the display cap.

Layout is `App/Helpers/Visuals/JustifiedRowPanel.cs`: every tab at its own text width, leftover space distributed
into the gaps, and no gaps once the content is wider than the viewport -- which is when the ScrollViewer's thin
horizontal bar appears. `Wheel` and `Tab`/`Shift+Tab` move along the row; `ScrollIntoView` follows the selection
because it arrives through a binding, not a click.
