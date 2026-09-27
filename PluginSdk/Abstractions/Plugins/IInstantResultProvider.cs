namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Represents a provider that can process the search query in real-time
/// and output instant results directly to the search result list.
/// </summary>
public interface IInstantResultProvider : IPluginComponent
{


    /// <summary>
    /// Processes the query and returns instant result items.
    /// Returns null or empty if this query is not handled by the provider.
    /// </summary>
    IEnumerable<InstantResultItem> GetInstantResults(string query);

    /// <summary>
    /// Returns a custom highlight mask if supported.
    /// </summary>
    bool[]? GetHighlightMask(string text, string query) => null;

    /// <summary>
    /// The leading word(s) the user types to invoke this provider ("cs", "ps", ...), exactly as the
    /// user has configured them -- never a hardcoded copy, and read fresh per call so a Settings change
    /// takes effect without reloading the plugin. Empty (the default) means "this provider has no
    /// trigger word", which is the case for every provider that matches on content rather than a prefix.
    /// </summary>
    /// <remarks>
    /// The host strips a claimed word before searching file/application names and before computing
    /// highlights, so typing "cs report" looks for files called "report" instead of fuzzy-matching the
    /// trigger itself and highlighting it in the result rows. <see cref="GetInstantResults"/> still
    /// receives the untouched box text: the provider that owns a word has to keep recognising it, so
    /// declaring the word here is what removes it from OTHER results, not from the provider's own.
    ///
    /// Where a word counts as typed is <see cref="Lertaro.PluginSdk.Services.TriggerWord"/>'s rule
    /// -- the same one the host
    /// applies, which is the point: a provider that recognised its word by some other rule could be
    /// silently out of sync with the strip. The one difference is deliberate and is each side's policy, not
    /// a drift: an <see cref="ISearchResultAction"/> keyword also activates BARE ("mkdir" with no argument)
    /// and completes a word still being typed ("mk"), because there the row IS what the user asked for,
    /// while a file search for the text "mkdir" is worth keeping until a term follows the word.
    /// </remarks>
    IReadOnlyList<string> QueryTriggerKeywords => [];
}

/// <summary>
/// Represents an individual instant result item to be displayed in the results list.
/// </summary>
public class InstantResultItem
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
    /// Optional pre-loaded icon as a GDI HBITMAP (Win32 handle), taking priority over IconData when
    /// set. The host takes ownership and calls DeleteObject once it's done with it -- do not reuse or
    /// free this handle yourself after handing it over. Mirrors SearchableItem.HBitmapIcon.
    /// </summary>
    public IntPtr HBitmapIcon { get; set; }

    /// <summary>
    /// Optional direct execution callback invoked when this result is selected.
    /// </summary>
    public Action? OnExecute { get; set; }

    /// <summary>
    /// Optional execution callback returning whether the search window should hide (true) or stay open (false).
    /// </summary>
    public Func<bool>? OnExecuteFunc { get; set; }
}
