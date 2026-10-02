# Core Search & Actions

This chapter covers the core interfaces and data structures in `Lertaro.PluginSdk` for contributing search data sources, instant calculation answers, non-ASCII alias engines, query suffix token handlers, and static/dynamic context action menus.

## 1. Base Component Specifications: `IPluginComponent` & `IPlugin`

All plugin components inherit directly or indirectly from `IPluginComponent`:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IPluginComponent
{
    string Name => GetType().Name;      // Display name (defaults to class name)
    string Description => string.Empty; // Description shown as a ToolTip in settings
}

public interface IPlugin : IPluginComponent
{
    // Primary plugin assembly entry point. Both website members are optional: with
    // WebsiteUrl null the Settings card shows no link at all.
    string? WebsiteUrl => null;
    string? WebsiteLabel => null;
}
```

## 2. Contributing Search Results

### Static Cacheable Item Provider `ISearchableItemProvider`

Ideal for relatively static or slow-to-enumerate items that do not change with every keystroke (e.g. Start Menu shortcuts, browser bookmarks, control panel items):

```csharp
public interface ISearchableItemProvider : IPluginComponent
{
    bool EnableAlias => true;           // Allow alias transliteration (e.g. pinyin)
    event Action? ItemsChanged;         // Trigger when items change to re-index
    IEnumerable<SearchableItem> GetSearchableItems();
}
```

### Dynamic Instant Calculation Provider `IInstantResultProvider`

Executes synchronously on every keystroke, ideal for results derived purely from the query string (e.g. calculators, base converters, URL jumpers):

```csharp
public interface IInstantResultProvider : IPluginComponent
{
    IEnumerable<InstantResultItem> GetInstantResults(string query);
    bool[]? GetHighlightMask(string text, string query) => null; // Custom highlight mask

    // The words that invoke this provider, read by the host's own strip step so its
    // stripping and your matching cannot drift apart. See section 6, "Trigger Words".
    IReadOnlyList<string> QueryTriggerKeywords => [];
}
```

> [!TIP]
> `GetInstantResults` is synchronous for typing fluidity. For async network queries (translation, web suggestions), return a placeholder item immediately, fetch data via `Task.Run` in the background, cache the result, and call `SearchRefreshService.RefreshIfMatches` to notify the host to refresh live results.

### Non-ASCII Alias Transliteration Engine `IAliasProvider`

Generates indexable transliteration aliases for non-ASCII text, supporting mixed pinyin/character matching:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IAliasProvider : IPluginComponent   // Name comes from IPluginComponent
{
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }  // Source range (e.g. CJK Ideographs)
    IReadOnlyList<(char Start, char End)> OutputRanges { get; } // Target range (e.g. a-z)

    // Set for a multi-syllable engine (pinyin): the character that joins syllables in the generated
    // alias. '\0' (the default) means the engine emits no separator at all.
    char SyllableSeparator => '\0';

    IEnumerable<string> GetAliases(string text);

    int Version => 1;                                           // Increment to trigger re-indexing
    IEnumerable<string> GetQueryForms(string term) => Array.Empty<string>(); // Query-side segmentation
    int[]? MapAliasToSourceIndices(string text, string alias) => null;       // Highlight mapping

    // Zero-allocation UTF-8 builder for the indexer's hot path. The default implementation forwards
    // GetAliases() into the sink, lower-casing only aliases that actually contain upper-case letters,
    // so an engine only overrides this when it can produce bytes cheaper than strings.
    void GetAliasesUtf8(string text, AliasByteSink dest);
}
```

### Query Suffix Token Handler `IQueryTokenProvider`

Claims and processes trailing tokens at the end of search queries (e.g. `report :size`, `doc :@today`, or `image ::"hello world"`), applying transformations (sorting, filtering) to matched results:

```csharp
public interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);

    // The text to keep highlighting in the result rows once this token has been consumed from the
    // query. Null (the default) leaves the host's own highlighting alone.
    string? GetHighlightText(string token) => null;
}
```

## 3. Context Actions on Results

### Action Provider Container `IActionProvider`

```csharp
public interface IActionProvider
{
    IEnumerable<ISearchResultAction> GetActions();
    IEnumerable<IDynamicActionProvider> GetDynamicActionProviders();
}
```

### Static Action Contract `ISearchResultAction`

Represents a standalone static operation (e.g. Copy Path, Run as Administrator) displayed in `Ctrl+O` action menus or bound to hotkeys:

```csharp
namespace Lertaro.PluginSdk.Abstractions;

public interface ISearchResultAction : Plugins.IPluginComponent
{
    string GroupName { get; }           // Group heading in the action menu
    string DisplayName { get; }         // Action title
    // Actions are addressed by their display name, so Name is mapped rather than authored:
    string Plugins.IPluginComponent.Name => DisplayName;

    // Non-nullable with a default. Empty string means "no shortcut", which is how the destructive
    // file actions stayed unbound before they were given Explorer's chords back.
    string Hotkey => string.Empty;
    IReadOnlyList<string> Keywords => Array.Empty<string>();
    IReadOnlyList<string> Parameters => Array.Empty<string>();

    // Where the action shows up. Default: visible in search, and visible in the menu only when it
    // claims no keyword (a keyword is how an action appears as a row instead).
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => true;
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => Keywords.Count == 0;

    ImageSource? Icon { get; }          // Action icon; null draws the group's default
    bool CanExecute(IReadOnlyList<ISearchResult> results);
    void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view);
}
```

### Dynamic Menu Builder `IDynamicActionProvider`

Constructs dynamic menus at runtime (such as embedding Windows Shell context menus):

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IDynamicActionProvider : IPluginComponent
{
    string GroupName { get; }
    string IPluginComponent.Name => GroupName;    // same mapping as ISearchResultAction

    int Priority => 0;                            // Menu ordering weight, not nullable
    IReadOnlyList<string> Keywords => Array.Empty<string>();
    IReadOnlyList<string> Parameters => Array.Empty<string>();

    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => true;
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => Keywords.Count == 0;

    void Init() { }                               // One-time warmup, called off the first menu
    bool CanProvide(IReadOnlyList<ISearchResult> results);

    // Opt in to menus over instant results (a window title, a process row). Default false, because
    // most providers key off file paths those rows do not have.
    bool CanProvideForInstantResults => false;

    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> results)
        => Array.Empty<(string, Action)>();
    void ExecuteCommand(IReadOnlyList<ISearchResult> results, uint commandId, IntPtr ownerHwnd);

    // NO default implementation: implementing this is mandatory. Called when the menu is torn down,
    // so a provider holding native handles or a cached shell CDS stream can release them.
    void ClearSession();
}
```

## 4. Supporting Models

- **`SearchableItem`**: Contains `Title`, `Description`, `IconData`, `IconColor`, `ActionType` (`"Copy"` / `"Execute"` / `"None"`), `ActionArgument`, `TabCompletion`, `HBitmapIcon` (auto-disposed by host), `ResultKind` (a plugin-chosen tag the host's filters and columns can key off), and two execute callbacks: `OnExecute` (`Action`) for fire-and-forget, or `OnExecuteFunc` (`Func<bool>`) when the action needs to report success — the host uses that answer, for example, to decide whether to close the window. `InstantResultItem` carries the same display and callback members **except `ResultKind`**, which only the searchable-item model has.
- **`DynamicMenuItem`**: Contains `Text`, `CommandId`, `IsSeparator`, `HasSubMenu`, `SubMenuHandle`, `IsDisabled`, `OnExecute`, `IsActionable` (default `true`; `false` marks a row that only opens a submenu), `HBitmapItem` (a native icon handle from the Shell menu being mirrored), `ShortcutHint` (the letter a mnemonic key matches), `IsContinuation` (a paging cursor: this batch continues a menu the host is still filling, and the host keeps asking while it is set), and `IsHeader` (renders as a group header with an optional action button).
- **`SearchWindowType`**: Enum with `Main`, `Quick`, and `Inline`.

## 5. Named Search Scopes `ISearchScopeProvider`

A **scope** is a keyword prefix plus a set of directories: typing `tf report` makes the host run its normal index search for `report` restricted to those folders. It is a second-stage filter over the existing index, not a second search engine.

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ISearchScopeProvider : IPluginComponent
{
    // Consulted on every keystroke dispatch: return a cached list, rebuilt only when the
    // configuration changes. Scopes with a blank keyword or no folders are ignored by the host.
    IReadOnlyList<SearchScope> GetSearchScopes();
}

public sealed class SearchScope
{
    public string Keyword { get; init; } = string.Empty;            // case-insensitive first token, e.g. "tf"
    public IReadOnlyList<string> Folders { get; init; } = Array.Empty<string>();
    public string FilterPattern { get; init; } = "*";               // ';'-separated Win32 wildcards on FILE names
}
```

Unlike `ISearchableItemProvider`, a scope provider never enumerates or materializes files, so memory and per-keystroke cost stay flat however large the configured folders are. A folder no host index covers is skipped with a logged warning rather than walked live — which is the same rule the indexer helpers follow: cover the folder in a configured local-drive, network, or folder index first. Directories always pass `FilterPattern`.

The in-repo implementation is the File Filters plugin.

## 6. Trigger Words `TriggerWord`

Any feature the user invokes by typing a leading word — an instant provider's `QueryTriggerKeywords`, an action's `Keywords`, a scope's `Keyword`, a file filter's trigger — resolves that word through `Lertaro.PluginSdk.Services.TriggerWord`, so the host's stripping and the plugin's matching cannot drift apart.

| Helper | Matches |
| :--- | :--- |
| `string Normalize(string? configured)` | Brings a configured word to the form every comparison expects: surrounding whitespace trimmed, empty for `null` or blank. Normalise on read — the host trims the word it strips, so comparing an untrimmed value recognises nothing while the host still removes the word from the file search. |
| `bool TryMatch(string query, string? word, out string argument)` | The first token **equals** the word (case-insensitive). `argument` is the remaining trimmed text, empty when the query is nothing but the word — which still matches. A longer word that merely starts with it does not (`csreport` is not `cs`). |
| `bool TryMatchInvoked(...)` | As above, but the word only counts once something has actually been typed after it. Use when the bare word would put rows on screen nobody asked for: `cs` alone stays a file search, `cs ` is the provider. |
| `bool TryMatchAny(string query, IReadOnlyList<string> words, out string matchedWord, out string argument)` | First matching word wins, in list order, and reports **which** one matched — a web-search engine with several keywords per provider needs that to re-parse. |
| `bool IsTypedPrefixOf(string query, string? word)` | The query is a still-unfinished typing of the word (`m` on the way to `mkdir`). The one branch allowed to offer a trigger before the whole word is there; false as soon as any separator is typed. |

`IInstantResultProvider.QueryTriggerKeywords` (empty by default) is what the host reads for its own strip step, and `PluginConfigField.IsTriggerWord` marks the `Text` setting that holds such a word so the Settings page can warn — without blocking the save — when another feature already answers to the same word. Two features on one word is otherwise silent: file search follows whichever registered first and the other's rows simply stop appearing, with nothing telling the user which one to rename.
