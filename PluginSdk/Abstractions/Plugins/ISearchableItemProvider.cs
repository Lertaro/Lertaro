namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Represents a static or slow-loading content provider that returns a full list
/// of searchable items. The host indexes and matches these items, handling highlighting.
/// </summary>
public interface ISearchableItemProvider : IPluginComponent
{


    /// <summary>
    /// Returns the complete list of searchable items.
    /// This may be cached by the host.
    /// </summary>
    IEnumerable<SearchableItem> GetSearchableItems();

    /// <summary>
    /// Whether alias matching (e.g., Pinyin alias matching) is enabled for items from this provider.
    /// Defaults to true.
    /// </summary>
    bool EnableAlias => true;

    /// <summary>
    /// The ResultKind every item from this provider carries, when they all share one (e.g. "Application").
    /// Null when the provider mixes kinds or does not declare one, in which case callers read each item's
    /// own <see cref="SearchableItem.ResultKind"/>. Hosts use this to name the type group a provider's rows
    /// belong to without depending on the provider implementation.
    /// </summary>
    string? ItemResultKind => null;

    /// <summary>
    /// Fired when the items of the provider change, notifying the host to clear cache.
    /// </summary>
    event Action? ItemsChanged;
}

/// <summary>Why a provider-supplied alias describes one searchable item.</summary>
public enum SearchableItemAliasKind : byte
{
    Curated = 0,
    ShortcutTarget = 1,
    ProductMetadata = 2,
}

/// <summary>An alias cached with a searchable item; matching never calls the provider again.</summary>
public readonly record struct SearchableItemAlias(string Text, SearchableItemAliasKind Kind = SearchableItemAliasKind.Curated);

/// <summary>
/// Represents an individual searchable item to be indexed by the host.
/// </summary>
public class SearchableItem
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Optional search keywords. Each query word must prefix a title or keyword word.</summary>
    public IReadOnlyList<string> Keywords { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional provider-owned aliases with provenance. The host snapshots these during provider loading;
    /// providers must not resolve shortcuts, inspect files, or perform other I/O from a query callback.
    /// </summary>
    public IReadOnlyList<SearchableItemAlias> Aliases { get; set; } = Array.Empty<SearchableItemAlias>();

    /// <summary>Optional vector path string for a custom icon.</summary>
    public string? IconData { get; set; }

    /// <summary>Optional custom hex color (e.g. #3399FF) for the vector icon.</summary>
    public string? IconColor { get; set; }

    /// <summary>
    /// Action to perform when double clicked or entered.
    /// "Copy" (copy ActionArgument to clipboard),
    /// "Execute" (run command/script),
    /// "None"
    /// </summary>
    public string ActionType { get; set; } = "Copy";

    /// <summary>The argument associated with the action (e.g. the text to copy).</summary>
    public string ActionArgument { get; set; } = string.Empty;

    /// <summary>Optional custom text to fill the search box with when Tab is pressed.</summary>
    public string? TabCompletion { get; set; }

    /// <summary>
    /// Optional pre-loaded icon as a GDI HBITMAP (Win32 handle). The host takes ownership and calls
    /// DeleteObject once it has materialized the bitmap -- do not reuse or free this handle yourself
    /// after handing it over.
    /// </summary>
    public IntPtr HBitmapIcon { get; set; }

    /// <summary>
    /// Optional direct execution callback delegate.
    /// </summary>
    public Action? OnExecute { get; set; }

    /// <summary>
    /// Optional execution callback returning whether the search window should hide (true) or stay open (false).
    /// </summary>
    public Func<bool>? OnExecuteFunc { get; set; }

    /// <summary>
    /// Optional custom ResultKind override (e.g. "Application", "File", "InstantResult").
    /// Defaults to "InstantResult".
    /// </summary>
    public string? ResultKind { get; set; }
}
