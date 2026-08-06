# UI & Preview Extensions

## Result display

### `ISidebarFilterProvider`

Adds categorizing filter groups to the results sidebar (e.g. date-range or size buckets).

```csharp
interface ISidebarFilterProvider
{
    int SortOrder { get; } // default 100; lower renders first
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}
```

`SidebarFilterGroup` has a `Header`, an `AllowMultiSelect` flag (default `false`; opts the group into
letting more than one item be selected at once, combined with OR — leave it off for items whose
meaning only makes sense one at a time, e.g. overlapping/cumulative date ranges), and a list of
`SidebarFilterItem`s (Id, DisplayName, optional icon, and an optional async `FilterPredicate` over the
current result list). The host shows a clear button on a group once it has a selection, so a provider
doesn't need an "All"/"Any" pseudo-item of its own.

### `IResultColumnProvider`

Injects extra columns into the results grid view (file size, modified date, custom metadata, ...).

```csharp
interface IResultColumnProvider
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}
```

`ResultColumnDefinition` carries a column id, header text, width, and optional
`VisibilityPredicate`/`SortComparer` delegates.

## Quick Panel

### `IQuickPanelTabProvider`

Contributes a whole tab to the [Quick Panel](../../user-guide/settings/quick-panel) — the floating
panel docked over whatever window is in front. The tab is named after the component and holds one
list, and the host renders the entries through its own result rows, so icons, opening, thumbnails and
the actions menu all come for free. CoreExtensions ships five: Favorites, History, Windows Recent
Items, Last Directory and Recent Files.

```csharp
interface IQuickPanelTabProvider : IPluginComponent
{
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

A tab rather than a group inside somebody else's tab: what a provider returns is a whole collection,
orthogonal to the folders a workspace gathers, so it sits beside them rather than having to be ticked
into each one.

`GetEntriesAsync()` is called every time the panel is summoned, and returns a finished set rather
than streaming: the panel orders and caps entries as a set (newest first, at most so many), so it
cannot show half of one without re-sorting on every arrival. That costs nothing in latency — every
tab loads on its own task and the panel opens on the first one to arrive, so a provider that has to
go and look delays only its own tab. Honour the token all the same: it is cancelled when the panel
closes.

Fill in `ISearchResult.Metadata`'s `Modified` where the source knows one — the default newest-first
order uses it, and entries without one keep the order they were returned in. A provider that returns
nothing gets no tab, and one that throws costs its own tab and nothing else.

The tab opens as thumbnail tiles unless the user ticks **Show as list** for it under Settings → Quick
Panel → Plugin tabs; the panel's own header toggle still overrides that for as long as it is open.
Closing a tab with its **×** is deliberately not the same as disabling the component in Settings →
Plugins: the first only takes it out of the strip (untick it back on that same page), the second
stops it loading at all. The host uses the component id as the stable key for both the closed state
and the display choice, so a tab closed while its plugin was off stays closed when it comes back.

## Preview & thumbnails

### `IFilePreviewProvider`

Renders a custom WPF `UIElement` in the QuickLook preview pane (see
[Actions Menu & Preview](../../user-guide/actions-and-preview#quicklook-preview)) for file types
you want to handle specially.

```csharp
interface IFilePreviewProvider
{
    string Name { get; }
    int Priority { get; } // default 0; higher runs first
    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);
    bool RendersExternally { get; } // default false
}
```

`Priority` is only the *default* order — the user can freely reorder providers (including relative
to yours) from Settings → General →
[Preview & Thumbnails](../../user-guide/settings/general#preview-thumbnails), which wins over
whatever `Priority` returns. Don't assume your provider's declared priority is the order it actually
runs in.

Two optional companion interfaces refine preview behavior:

- **`IPreviewSessionAware`** — implement this on the preview provider itself if it holds onto
  expensive out-of-process resources (a hosted native handler, a file lock); `EndPreviewSession()`
  is called once the whole preview session ends, not on every individual preview swap. The one
  exception: for a provider with `RendersExternally` true, the host calls it on every swap away
  from that provider too, not just session end — see below.
- **`IReusablePreview`** — implement this on the `UIElement` returned from `CreatePreview` if it
  can re-point itself at a new file instead of being rebuilt from scratch: `TrySetTarget(path,
  isDir)` returns `true` if it handled the change in place, `false` to tell the host to build a
  fresh preview instead.

`RendersExternally` is for a provider whose real preview surface is a separate, externally-managed
window rather than the `UIElement` `CreatePreview` returns — e.g. handing the file off to another
application entirely. When the winning provider has this set, the host hides its own preview panel
instead of displaying `CreatePreview`'s content (which is then never actually shown, so it can be
a trivial placeholder). Pair it with **`IReceivesPreviewPanelBounds`** to get the exact screen
rectangle (physical pixels) the host's own panel would have occupied, so the external window can be
positioned there instead of wherever it would otherwise appear:

```csharp
interface IReceivesPreviewPanelBounds
{
    void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);
}
```

See the bundled (experimental) QuickLook Bridge plugin for a real example: it detects an external
[QuickLook](https://github.com/QL-Win/QuickLook) app over its own named pipe and, if reachable,
docks that app's window into the host panel's spot for every file/folder — see [Actions Menu &
Preview → External preview via QuickLook](../../user-guide/actions-and-preview#external-preview-via-quicklook-optional)
for the user-facing behavior. Note this is a different thing from Lertaro's own built-in preview
pane, which is also informally called "QuickLook" throughout this codebase and docs.

### `IThumbnailProvider`

Overrides the icon/thumbnail shown for matching results.

```csharp
interface IThumbnailProvider : IPluginComponent
{
    int Priority { get; } // default 0; higher runs first
    bool CanProvideThumbnail(string path, bool isDir);
    ImageSource? GetThumbnail(string path, int size);
}
```

Same caveat as `IFilePreviewProvider.Priority` above: it's only the default order, and the user can
override it from Settings → General →
[Preview & Thumbnails](../../user-guide/settings/general#preview-thumbnails) (the same tab hosts both
providers' order lists).

## Themes & localization

### `IThemeProvider` / `ITheme`

Registers one or more custom WPF resource dictionaries as selectable themes (shown in
**Settings → General → Interface theme**).

```csharp
interface IThemeProvider
{
    string Name { get; }
    IEnumerable<ITheme> GetThemes();
}

interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    double WindowOpacity { get; } // default 1.0
    ResourceDictionary GetResources();
}
```

### `ITranslationProvider`

Supplies UI strings for a given culture — for the plugin's own UI, or (as with `PinyinAlias`) just
its own display name. See [Example Plugins](../examples) for a plugin that implements this
alongside an unrelated interface on the same class.

```csharp
interface ITranslationProvider
{
    string Name { get; }
    IReadOnlyList<string> SupportedCultures { get; } // e.g. "zh-CN", "en-US"
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```

`TranslationService.LoadEmbeddedTranslations` (see [Host Services](./services)) is the standard way
to back this with JSON files embedded in your plugin DLL.
