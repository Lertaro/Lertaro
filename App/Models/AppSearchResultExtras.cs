using System.Windows;

namespace Lertaro.App;

/// <summary>Lazily allocated state not stored on an index-backed <see cref="AppSearchResult"/>.</summary>
internal sealed class AppSearchResultExtras
{
    // Overrides for what would otherwise be read off the source record. Set by the object-initializer
    // construction that synthetic rows (section headers, "no results", plugin actions, favorites,
    // instant results) use, where there is no source record to read from in the first place.
    public string? Name;
    public string? FullPath;
    public string? ParentDir;
    public string? ContextDirectory;
    public string? Drive;
    public bool? IsDir;
    public PluginSdk.Abstractions.FileMetadata? Metadata;

    /// <summary>
    /// The directory scope a scoped search was run under, which changes how ParentDir is displayed
    /// (a path relative to the scope rather than an absolute one). Only the quick window's scoped
    /// searches set it, and those are capped at a few dozen rows.
    /// </summary>
    public string? Scope;

    // Plugin and instant-result rows only.
    public uint PluginActionId;
    public string PluginActionArgumentText = string.Empty;
    public System.Windows.Media.ImageSource? IconOverride;
    public string InstantResultActionType = "Copy";
    public string InstantResultActionArgument = string.Empty;
    public Action? InstantResultOnExecute;
    public Func<bool>? InstantResultOnExecuteFunc;
    public string? TabCompletion;
    public object? SourceProvider;

    // Final quick-search diagnostics; unset for full-search rows.
    public Core.SearchIndex.RankingExplanation? RankingExplanation;

    // Set only on rows the quick window injected from a learned query->path association.
    public bool IsLearnedAliasRecall;

    // Filled in once the grid realizes the row and it starts loading its own visuals.
    public System.Windows.Media.ImageSource? Icon;
    public bool IconLoadingStarted;
    public string ShortcutHint = string.Empty;
    public Visibility ShortcutVisibility = Visibility.Collapsed;
    public DateTime? DateModified;
    public bool DateModifiedLoadingStarted;
    public Dictionary<string, string>? ExtendedValues;
}
