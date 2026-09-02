# Core Search & Actions

This chapter covers the core interfaces and data structures in `Lertaro.PluginSdk` for contributing search data sources, instant calculation answers, non-ASCII alias engines, query suffix token handlers, and static/dynamic context action menus.

## 1. Base Component Specifications: `IPluginComponent` & `IPlugin`

All plugin components inherit directly or indirectly from `IPluginComponent`:

```csharp
namespace Lertaro.PluginSdk;

public interface IPluginComponent
{
    string Name => GetType().Name;      // Display name (defaults to class name)
    string Description => string.Empty; // Description shown as a ToolTip in settings
}

public interface IPlugin : IPluginComponent
{
    // Primary plugin assembly entry point
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
}
```

> [!TIP]
> `GetInstantResults` is synchronous for typing fluidity. For async network queries (translation, web suggestions), return a placeholder item immediately, fetch data via `Task.Run` in the background, cache the result, and call `SearchRefreshService.RefreshIfMatches` to notify the host to refresh live results.

### Non-ASCII Alias Transliteration Engine `IAliasProvider`

Generates indexable transliteration aliases for non-ASCII text, supporting mixed pinyin/character matching:

```csharp
public interface IAliasProvider
{
    string Name { get; }
    bool CanHandle(string text);
    IReadOnlyList<(char Start, char End)> InputRanges { get; }  // Source range (e.g. CJK Ideographs)
    IReadOnlyList<(char Start, char End)> OutputRanges { get; } // Target range (e.g. a-z)
    IEnumerable<string> GetAliases(string text);

    int Version => 1;                                           // Increment to trigger re-indexing
    int[]? MapAliasToSourceIndices(string text, string alias) => null; // Highlight mapping
    void GetAliasesUtf8(string text, AliasByteSink dest);       // Zero-allocation UTF-8 builder
    IEnumerable<string> GetQueryForms(string term);             // Query-side segmentation
}
```

### Query Suffix Token Handler `IQueryTokenProvider`

Claims and processes trailing tokens at the end of search queries (e.g. `report :size`, `doc :@today`, or `image ::"hello world"`), applying transformations (sorting, filtering) to matched results:

```csharp
public interface IQueryTokenProvider : IPluginComponent
{
    bool CanHandle(string token);
    Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results);
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
public interface ISearchResultAction : IPluginComponent
{
    string GroupName { get; }           // Group name
    string DisplayName { get; }         // Action title
    string? Hotkey { get; }             // Default shortcut (e.g. "Ctrl+Shift+C")
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    ImageSource Icon { get; }           // Action icon
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool CanExecute(IReadOnlyList<ISearchResult> selection);
    void Execute(IReadOnlyList<ISearchResult> selection, IPluginSearchWindow window);
}
```

### Dynamic Menu Builder `IDynamicActionProvider`

Constructs dynamic menus at runtime (such as embedding Windows Shell context menus):

```csharp
public interface IDynamicActionProvider
{
    string GroupName { get; }
    int? Priority => 0;                 // Menu display priority
    IReadOnlyList<string>? Keywords { get; }
    IReadOnlyList<string>? Parameters { get; }
    bool IsVisibleInSearch(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    bool IsVisibleInMenu(IReadOnlyList<ISearchResult> selection, SearchWindowType windowType);
    void Init() { }                     // One-time warmup initialization
    bool CanProvide(IReadOnlyList<ISearchResult> selection);
    IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> selection, IntPtr hMenu);
    IEnumerable<(string Hotkey, Action Execute)> GetHotkeyActions(IReadOnlyList<ISearchResult> selection);
    void ExecuteCommand(IReadOnlyList<ISearchResult> selection, uint commandId, IntPtr ownerHwnd);
    void ClearSession() { }
}
```

## 4. Supporting Models

- **`SearchableItem` / `InstantResultItem`**: Contains `Title`, `Description`, `IconData`, `IconColor`, `ActionType` (`"Copy"` / `"Execute"` / `"None"`), `ActionArgument`, `TabCompletion`, `HBitmapIcon` (auto-disposed by host), and `OnExecute` callback.
- **`DynamicMenuItem`**: Contains `Text`, `CommandId`, `IsSeparator`, `HasSubMenu`, `SubMenuHandle`, `IsDisabled`, `IsActionable`, `IsContinuation`, `OnExecute`, `IsHeader`, and `ShortcutHint`. Set `IsActionable = false` for a pure category node that only opens a submenu; actionable folder nodes can keep the default `true`. `IsContinuation = true` marks a paged submenu cursor that the host loads automatically instead of rendering as a visible "load more" row. `IsHeader` renders a group header with an optional action button.
- **`SearchWindowType`**: Enum with `Main`, `Quick`, and `Inline`.
