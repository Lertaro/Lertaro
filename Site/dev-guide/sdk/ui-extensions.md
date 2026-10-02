# UI & Preview Extensions

This chapter introduces `Lertaro.PluginSdk` interfaces for extending the search window sidebar, adding custom table columns, providing dynamic Quick Panel tabs, building QuickLook file previewers and thumbnail extractors, and shipping WPF themes and i18n localization packs.

All of these live under `Lertaro.PluginSdk.Abstractions.Plugins` (preview providers under `…Abstractions.Plugins.Preview`) and every one of them derives from `IPluginComponent`, which supplies the `Name` the host shows in **Settings → Plugins**.

## 1. Sidebar Filter Provider `ISidebarFilterProvider`

Injects custom filter categories into the left sidebar of the search window:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ISidebarFilterProvider : IPluginComponent
{
    IEnumerable<SidebarFilterGroup> GetFilterGroups();

    // Ordering weight; lower values render first.
    int SortOrder => 100;
}

public class SidebarFilterGroup
{
    // Optional stable id the host recognises for well-known groups (e.g. "Type" for the
    // built-in result-type filter). Empty when the group is entirely plugin-defined.
    public string Id { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public List<SidebarFilterItem> Items { get; set; } = new();

    // Whether several items in this group can be active at once.
    public bool AllowMultiSelect { get; set; }
}

public class SidebarFilterItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // Two icon routes, both theme-aware: IconData is a glyph drawn in the active theme's
    // text colour, IconKey names a resource the host already owns. Leave both null for none.
    public string? IconData { get; set; }
    public string? IconKey { get; set; }

    // The predicate a result must satisfy for this item to match. Defaults to "matches nothing",
    // so an item that never sets it is shown but can never select anything.
    public Func<ISearchResult, bool> MatchPredicate { get; set; } = _ => false;
}
```

Groups and items are mutable classes, not records: fill in the properties you need and leave the rest at their defaults.

## 2. Custom Table Column Provider `IResultColumnProvider`

Appends custom data columns to the "Details" table view of the Full Search window (e.g. media duration, lines of code, Git branch). The provider describes its columns once and answers per-cell values on demand:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IResultColumnProvider : IPluginComponent
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}

public class ResultColumnDefinition
{
    public string ColumnId { get; set; } = string.Empty;
    public string HeaderText { get; set; } = string.Empty;
    public double Width { get; set; } = 120;

    // Optional: hide the column for results it does not apply to.
    public Func<ISearchResult, bool>? VisibilityPredicate { get; set; }

    // Optional: custom sort on header click. Negative when x < y, positive when x > y.
    public Func<ISearchResult, ISearchResult, int>? SortComparer { get; set; }

    // Optional: a double-click on this column's cell in the Full window. Leave it unset and
    // double-clicking the cell behaves like double-clicking anywhere else on the row.
    public Action<ISearchResult>? OnDoubleClick { get; set; }
}
```

`GetCellValue` is called during list rendering, so it must be cheap; hand back pre-computed values or read a cache rather than touching disk.

## 3. Quick Panel Tab Provider `IQuickPanelTabProvider`

Contributes a dynamic workspace tab to the [**Quick Panel**](../../user-guide/settings/quick-panel):

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IQuickPanelTabProvider : IPluginComponent
{
    // The entries to show right now. Called each time the panel is summoned.
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

That single method is the whole contract — there is no drop handling, reordering, or action context to implement.

- The `CancellationToken` is cancelled when the panel closes. Only the tab's own list observes it; nothing else about your plugin changes.
- Fill in `ISearchResult`'s `Metadata.Modified` where the source knows one, because the default newest-first ordering uses it. Leave it at its default and the entries keep the order you returned them in.
- A provider that returns nothing gets no tab, and there is nothing to configure for that.
- The tab exists as soon as the plugin does, unlike a folder the user has to add. It can be closed from the strip and reopened under **Settings → Quick Panel**, which is a different question from disabling the component under **Settings → Plugins** (that stops it loading at all).

## 4. File Previews & Thumbnails

### Custom File Preview Provider `IFilePreviewProvider`

Renders previews inside the QuickLook panel, which the user opens with `Alt+P` or a middle-click on a previewable row (see [**Actions & Preview**](../../user-guide/actions-and-preview)):

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

public interface IFilePreviewProvider : IPluginComponent
{
    // Breaks ties only. The user's configured provider order (Settings → General →
    // Previews & Thumbnails) is applied first; Priority sorts inside it, higher first.
    int Priority => 0;

    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);

    // True when the provider hosts an external window of its own rather than returning WPF
    // content to be laid out inside the panel (the QuickLook bridge plugin does this).
    bool RendersExternally => false;
}
```

#### Preview Lifecycle & Reuse Contracts

When your **provider** implements the first contract below, or the `UIElement` you return implements the second, the host optimizes the preview lifecycle:

- **`IPreviewSessionAware`** — cast on the **provider**, not on the control it returned: `void EndPreviewSession();`. The provider owns a real external window (an `HwndHost`, a native `IPreviewHandler` and its `prevhost` surrogate), not just an in-process control, so it is told to end its session when the owner window closes, and — for providers that render in-process — only when the preview panel hides or the session ends. Without this the host's window would linger with nothing pointing at it.
- **`IReusablePreview`** — cast on the returned element: `bool TrySetTarget(string path, bool isDir);`. When the user steps between similar files with the arrow keys, the host asks the same control to retarget instead of destroying and rebuilding it, which is what removes the flicker. Return `false` when the new target does not suit this instance and the host falls back to building a fresh preview.
- **`IReceivesPreviewPanelBounds`** — `void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);`. A provider hosting its own out-of-process window needs to know the rectangle the panel occupies so it can parent or position into it; implement this to be handed that rectangle once it is known.

### Custom Thumbnail Provider `IThumbnailProvider`

Extracts thumbnails for formats with no native Shell handler (`.blend`, `.psd`, `.dwg`):

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

public interface IThumbnailProvider : IPluginComponent
{
    // Same rule as previews: the user's configured thumbnail-provider order decides first,
    // Priority only sorts within it.
    int Priority => 0;

    bool CanProvideThumbnail(string path, bool isDir);

    // Synchronous, because it runs on the result-list rendering path -- keep it fast.
    // You do NOT have to memoise: the host caches what you return (keyed by path for a
    // physical or virtual item, by extension otherwise). Two consequences: `size` is the
    // host's own choice, taken from the shell image list, so do not expect a particular
    // number; and a provider is never asked about a directory at all.
    ImageSource? GetThumbnail(string path, int size);
}
```

## 5. Themes & Localization

### Theme Provider `IThemeProvider`

Contributes color palettes and WPF resource dictionaries:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface IThemeProvider : IPluginComponent
{
    IEnumerable<ITheme> GetThemes();
}
```

```csharp
namespace Lertaro.PluginSdk.Abstractions;   // note: the theme itself is one level up

public interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    ResourceDictionary GetResources();

    // Below 1.0 a window built through the host's layered-surface helper becomes a layered,
    // translucent window whose corner has to be painted and clipped; at 1.0 it stays opaque,
    // is rounded by the window manager, and keeps ClearType. The choice is made once, in the
    // window's constructor, because AllowsTransparency cannot change after the handle exists.
    // Applied to the notification windows today; a theme switch while a window is on screen
    // does not rebuild it.
    double WindowOpacity => 1.0;
}
```

One provider can contribute any number of themes, and each theme carries its own light-or-dark flag rather than the provider exposing a dark variant.

### Localization Provider `ITranslationProvider`

Supplies translation dictionaries dynamically:

```csharp
namespace Lertaro.PluginSdk.Abstractions.Plugins;

public interface ITranslationProvider : IPluginComponent
{
    // The culture codes this provider can serve, so the host can offer them in Settings
    // before anything is loaded. Empty by default, which means "discover from what is asked".
    IReadOnlyList<string> SupportedCultures => Array.Empty<string>();

    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```
