using System.Windows;

namespace Lertaro.App;

/// <summary>
/// Everything an <see cref="AppSearchResult"/> needs beyond the index record it came from, allocated
/// only for the rows that actually need it.
/// </summary>
/// <remarks>
/// Most rows in a large result set are never looked at. They exist because the grid's scrollbar has to
/// know how far it goes, and nothing reads a single property on them -- so anything stored per row is
/// paid six hundred thousand times to answer no questions. What is left on the row itself is the
/// handful of fields every row genuinely needs; everything here is either a synthetic row's own values
/// (there are never many of those) or something a row caches once the grid realizes it (there are never
/// many of those on screen at a time either).
///
/// Every field is nullable, or has a default the row returns when there is no Extras at all, so that
/// "not allocated" and "allocated but untouched" are the same answer.
/// </remarks>
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
    public string? TabCompletion;
    public object? SourceProvider;

    // Filled in once the grid realizes the row and it starts loading its own visuals.
    public System.Windows.Media.ImageSource? Icon;
    public bool IconLoadingStarted;
    public string ShortcutHint = string.Empty;
    public Visibility ShortcutVisibility = Visibility.Collapsed;
    public DateTime? DateModified;
    public bool DateModifiedLoadingStarted;
    public Dictionary<string, string>? ExtendedValues;
}
