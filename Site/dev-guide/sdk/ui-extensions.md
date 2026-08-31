# UI & Preview Extensions

This chapter introduces `Lertaro.PluginSdk` interfaces for extending the main sidebar, adding custom table columns, providing dynamic Quick Panel tabs, crafting QuickLook file previewers, extracting thumbnails, and building WPF themes and i18n localization packs.

## 1. Sidebar Filter Provider `ISidebarFilterProvider`

Injects custom category filter trees into the left sidebar of the Full Search window:

```csharp
namespace Lertaro.PluginSdk;

public interface ISidebarFilterProvider : IPluginComponent
{
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}

public sealed class SidebarFilterGroup
{
    public required string GroupName { get; init; }
    public required IReadOnlyList<SidebarFilterItem> FilterItems { get; init; }
}

public sealed class SidebarFilterItem
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public ImageSource? Icon { get; init; }
    public required Func<ISearchResult, bool> FilterFunc { get; init; } // Match predicate
}
```

`SidebarFilterGroup.Id` is an optional stable group identifier. The host can use recognised identifiers such as `Type` to apply built-in behaviour; leave it empty when the group is entirely plugin-defined.

## 2. Custom Table Column Provider `IResultColumnProvider`

Appends custom data columns to the "Details" table view of the Full Search window (e.g. displaying media duration, lines of code, or Git branches):

```csharp
public interface IResultColumnProvider : IPluginComponent
{
    string ColumnId { get; }
    string HeaderText { get; }
    double DefaultWidth => 120;
    double MinWidth => 40;
    bool IsVisibleByDefault => false;
    string? GetCellText(ISearchResult result);
    int Compare(ISearchResult a, ISearchResult b) => 0; // Header click sort comparer
}
```

## 3. Quick Panel Tab Provider `IQuickPanelTabProvider`

Contributes dynamic workspace tabs to the [**Quick Panel**](../../user-guide/settings/quick-panel) under the Quick Search bar:

```csharp
public interface IQuickPanelTabProvider : IPluginComponent
{
    string TabId { get; }
    string Title { get; }
    string? IconPath => null;
    Task<IReadOnlyList<ISearchResult>> GetItemsAsync(CancellationToken token);

    // Drag-and-drop acceptance logic
    bool CanHandleDragOver(IDataObject data) => false;
    Task HandleDropAsync(IDataObject data, CancellationToken token) => Task.CompletedTask;

    // Drag-to-reorder support
    bool SupportsReorder => false;
    Task SaveOrderAsync(IReadOnlyList<ISearchResult> orderedItems) => Task.CompletedTask;

    // Custom tab action context
    DynamicActionContext CreateActionContext() => DynamicActionContext.Default;
}
```

## 4. File Previews & Thumbnails

### Custom File Preview Provider `IFilePreviewProvider`

Renders interactive previews inside the QuickLook window (triggered via `Space`):

```csharp
public interface IFilePreviewProvider : IPluginComponent
{
    bool CanPreview(string filePath);
    int Priority => 0;                  // Priority when multiple providers match
    FrameworkElement CreatePreviewControl(string filePath);
}
```

#### Preview Lifecycle & Reuse Contracts

When your returned WPF `FrameworkElement` implements the following optional contracts, the host optimizes the preview lifecycle:

- **`IPreviewSessionAware`**: Implements `void OnPreviewClosed()`, triggered when the preview window closes or navigates to an incompatible file, ensuring safe disposal of media players, WebView2 instances, or file streams.
- **`IReusablePreview`**: Implements `void UpdatePreview(string filePath)`. When navigating continuously between similar files via arrow keys, the host updates content in-place without destroying and recreating the control, eliminating UI flicker.

### Custom Thumbnail Provider `IThumbnailProvider`

Extracts high-resolution thumbnails for proprietary formats without native Shell thumbnail handlers (e.g. `.blend`, `.psd`, `.dwg`):

```csharp
public interface IThumbnailProvider : IPluginComponent
{
    bool CanProvide(string filePath);
    Task<ImageSource?> GetThumbnailAsync(string filePath, int targetSize, CancellationToken token);
}
```

## 5. Themes & Localization

### Theme Provider `IThemeProvider`

Contributes custom color palettes and WPF Resource Dictionaries to Lertaro:

```csharp
public interface IThemeProvider : IPluginComponent
{
    string ThemeId { get; }
    string DisplayName { get; }
    ResourceDictionary GetResourceDictionary(bool isDark);
}
```

### Localization Provider `ITranslationProvider`

Supplies localized translation dictionaries dynamically:

```csharp
public interface ITranslationProvider : IPluginComponent
{
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```
