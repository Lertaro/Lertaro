# Core Search & Actions

## `IPluginComponent` & `IPlugin`

All plugin components (including the plugin entry class itself) must inherit from `IPluginComponent`. This interface provides the component's name and description:

```csharp
interface IPluginComponent
{
    string Name => GetType().Name;       // Component display name, defaults to type name
    string Description => string.Empty;  // Component description/tooltip shown in settings UI
}
```

Every plugin must implement the `IPlugin` interface (inheriting from `IPluginComponent`) as the main entry point, plus whichever other interfaces it needs:

```csharp
interface IPlugin : IPluginComponent
{
}
```

## Contributing search results

### `ISearchableItemProvider`

Returns a full, cacheable list of items to fold into the index — for content that's static or
slow to enumerate but doesn't change every keystroke (e.g. Start Menu shortcuts, a bookmark list).

```csharp
interface ISearchableItemProvider : IPluginComponent
{
    bool EnableAlias { get; } // default true
    event Action? ItemsChanged;
    IEnumerable<SearchableItem> GetSearchableItems();
}
```

### `IInstantResultProvider`

Runs on every keystroke and returns results directly — for query-shaped content like a calculator
or a URL shortcut, not something you'd want indexed ahead of time.

```csharp
interface IInstantResultProvider : IPluginComponent
{
    IEnumerable<InstantResultItem> GetInstantResults(string query);
    bool[]? GetHighlightMask(string text, string query); // optional match highlighting
}
```

`GetInstantResults` is synchronous only — there's no async/cancellation-token overload. If your data
needs a network round-trip (translating text, fetching search-engine suggestions), return a
placeholder item immediately, kick off the real work with `Task.Run`, cache the result once it lands,
and call `SearchRefreshService.RefreshIfMatches` (see [Host Services](./services)) so the host re-runs
any search whose current query would now hit your cache — see the WebSearch plugin's suggestion
fetching (`Plugins/WebSearch/WebSearchInstantProvider.cs`) for a worked example.

### `IAliasProvider`

Generates extra searchable strings for non-ASCII text — this is how pinyin aliasing for Chinese
filenames works (see [PinyinAlias](../examples#pinyinalias-pinyin-aliasing-for-chinese-filenames)).

```csharp
interface IAliasProvider
{
    string Name { get; }
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }
    IReadOnlyList<(char Start, char End)> OutputRanges { get; }
    IEnumerable<string> GetAliases(string text);

    int Version { get; } // default 1
    int[]? MapAliasToSourceIndices(string text, string alias); // default null
    void GetAliasesUtf8(string text, AliasByteSink dest); // default: adapts GetAliases
    IEnumerable<string> GetQueryForms(string term); // default: none
}
```

`InputRanges` and `OutputRanges` have no default — every provider must declare them.
`InputRanges` is the character range(s) this provider transliterates *from* (e.g. the CJK ideograph
block, for pinyin); `OutputRanges` is the range(s) its generated aliases are made of (e.g. lowercase
`a`-`z`). The host uses the two together to segment a query term that mixes a provider's own input and
output alphabets (e.g. `大cj` against a candidate `大长今`) into a literal run matched against the
candidate's own text and an alias-syntax run matched against this provider's alias, instead of
guessing at ASCII-ness.

`Version`, `MapAliasToSourceIndices`, and `GetAliasesUtf8` are all default-implemented — most
providers never need to touch them:

- **`Version`**: bump it when this provider's output could change for the same input (an algorithm
  fix, a new rule, an updated data table). The index uses it to detect that previously-generated
  aliases from this provider are stale and need regenerating.
- **`MapAliasToSourceIndices`**: lets a match found against an alias (e.g. which pinyin letters
  matched) be translated back onto the original text for highlighting, instead of highlighting
  nothing because the query never appears verbatim in the untransliterated text. Return `null`
  (the default) if this alias wasn't produced by this provider for this text, or mapping isn't
  supported — the host treats that as "can't highlight via this provider," not an error.
- **`GetAliasesUtf8`**: byte-native variant used on the host's bulk indexing path, where aliases are
  ultimately stored as UTF-8 bytes. The default implementation adapts `GetAliases`, so existing
  providers work unchanged; only override it to skip string materialization entirely when your
  provider generates a very high volume of aliases and that allocation cost actually shows up in
  practice.
- **`GetQueryForms`**: the query-side counterpart of `GetAliases` — rewrites one typed query term
  into whatever delimited shape this provider's own aliases use, so a term the user typed as one
  run of plain characters can still respect structure the host doesn't understand (pinyin syllable
  boundaries, for example, which is what keeps a query from matching across two unrelated
  syllables). Returning nothing (the default) means "this term isn't in my alphabet at all," which
  is what stops a query the provider can't express from reaching aliases it was never meant to
  match. Called once per term per query, never per candidate, so real work here is affordable —
  but every form returned becomes another alternative every candidate is tested against, so
  returning many is what costs.

### `IQueryTokenProvider`

Claims a trailing token from the query (e.g. `report :size`, `report ::"hello world"`, or `report ::hello\ world`) and transforms the already-matched result list — sorting, filtering, or otherwise composing on top of a normal search. Supports spaces in tokens enclosed in double/single quotes (`"..."`, `'...'`) or with escaped spaces (`\ `).

```csharp
interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);
}
```

## Actions on results

### `IActionProvider`

The container a plugin implements to expose both static and dynamic actions:

```csharp
interface IActionProvider
{
    IEnumerable<ISearchResultAction> GetActions();
    IEnumerable<IDynamicActionProvider> GetDynamicActionProviders();
}
```

### `ISearchResultAction`

A single, static action (e.g. "Copy Path") shown in the Actions menu or the quick-window action
hotkeys:

```csharp
interface ISearchResultAction : IPluginComponent
{
    string GroupName { get; }
    string DisplayName { get; }
    string? Hotkey { get; }              // optional default hotkey
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    ImageSource Icon { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool CanExecute(IReadOnlyList<ISearchResult> selection);
    void Execute(IReadOnlyList<ISearchResult> selection, IPluginSearchWindow window);
}
```

### `IDynamicActionProvider`

Builds menu items at runtime instead of returning a fixed list — this is how the real Windows
shell right-click menu (with nested cascade submenus) gets surfaced inside Lertaro's Actions
menu; see [ShellMenuActionProvider](../examples#coreextensions-actions-and-the-shell-context-menu).

```csharp
interface IDynamicActionProvider
{
    string GroupName { get; }
    int? Priority { get; }
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    void Init();
    bool CanProvide(IReadOnlyList<ISearchResult> selection);
    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> selection, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> selection);
    void ExecuteCommand(IReadOnlyList<ISearchResult> selection, uint commandId, IntPtr ownerHwnd);
    void ClearSession();
}
```

`Init()` is called by the host at most once per process, the first time any actions menu opens —
before `CanProvide`/`GetMenuItems` are actually invoked for any selection. The host guarantees the
at-most-once part, so an implementation doesn't need to guard against repeat calls itself. Use it
for slow one-time setup (e.g. warming up a native worker thread) that benefits from a genuine head
start instead of racing your own `CanProvide`/`GetMenuItems` call, which follow immediately after
with no lead time of their own — must not block, so do any real work on a background thread.
Default implementation is a no-op.

`Priority` controls this provider's own section among the actions menu's dynamic (per-provider)
groups: lower values appear first, default `0`. It's only a fallback, though — a user can drag/reorder
these sections by hand under
[Settings → General → Full Search Window](../../user-guide/settings/general#full-search-window), and
a section they've explicitly ordered keeps that position regardless of `Priority`.

## Supporting models

- **`SearchableItem`** / **`InstantResultItem`** — share Title, Description, IconData, IconColor,
  ActionType (`"Copy"` / `"Execute"` / `"None"`), ActionArgument, TabCompletion, and `HBitmapIcon` (a
  pre-loaded GDI HBITMAP that takes priority over IconData when set — the host takes ownership and
  calls DeleteObject once it's done with it, so don't reuse or free the handle yourself afterward;
  see the Window Switcher plugin's own window-thumbnail capture for a worked example).
  `SearchableItem` additionally has `OnExecute` (a direct invocation delegate) and `ResultKind`
  (override, e.g. `"Application"`/`"File"`).
- **`DynamicMenuItem`** — Text, CommandId, IsSeparator, HasSubMenu, SubMenuHandle, IsDisabled,
  HBitmapItem, OnExecute, ShortcutHint, IsHeader. `IsHeader` renders the item as a non-clickable
  section-header row (like a Quick Navigation submenu's own group name) instead of a normal row —
  Text is the header label, and if `OnExecute` is also set, a small button appears at the header's
  trailing edge invoking it; every other field is ignored when `IsHeader` is true. This is the
  submenu-depth equivalent of [`IQuickNavigationProvider.HeaderAction`](./system-adapters#iquicknavigationprovider),
  which only covers the root level.
- **`SearchWindowType`** enum — `Main`, `Quick`, `Inline`. Lets an action or provider behave
  differently depending on which of the three windows (see the
  [User Manual](../../user-guide/getting-started#the-three-windows)) it's showing in.
