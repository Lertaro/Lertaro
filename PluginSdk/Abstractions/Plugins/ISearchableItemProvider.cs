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
    /// Fired when the items of the provider change, notifying the host to clear cache.
    /// </summary>
    event Action? ItemsChanged;
}

/// <summary>
/// Represents an individual searchable item to be indexed by the host.
/// </summary>
public class SearchableItem
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

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
    /// Optional pre-loaded icon as a GDI HBITMAP (Win32 handle).
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
