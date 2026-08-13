using System.Windows;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Search;
namespace Lertaro.App;

/// <summary>
/// One row of a result list.
/// </summary>
/// <remarks>
/// A row built from the index keeps the record it came from and derives its displayed values from that
/// on demand, rather than copying them out at construction. It used to copy: measured over 300,000
/// rows, each one cost 577 bytes, of which 353 were strings it had built and would hold for the life of
/// the search. Two thirds of those were a duplicate -- the parent directory was computed once here and
/// again inside GetParentDisplayText, and both copies were kept.
///
/// That was affordable when the full window showed a page of a thousand results. It is not now that it
/// shows every match on the drive: a single-letter query returns six hundred thousand rows, of which the
/// grid ever realizes a few dozen, and the rest were paying 366MB to hold strings nobody would read.
///
/// Everything a row needs beyond its source record lives in a lazily-allocated
/// <see cref="AppSearchResultExtras"/> -- see there for what and why. A synthetic row (a section header,
/// a plugin action, "no results") has no source record and writes through the same property setters,
/// which allocate one; there are never many of those. A row the grid realizes allocates one too, to
/// cache its icon and its parent-directory text; there are never many of those on screen at once.
/// </remarks>
public class AppSearchResult : System.ComponentModel.INotifyPropertyChanged, PluginSdk.Abstractions.ISearchResult
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));

    // The index record this row displays, for rows that came from a search. Null for synthetic rows,
    // which carry their own values in Extras instead.
    private Core.SearchResult? _source;
    private AppSearchResultExtras? _extras;

    private AppSearchResultExtras Extras => _extras ??= new AppSearchResultExtras();
    internal AppSearchResultExtras DisplayExtras => Extras;
    internal AppSearchResultExtras? ExistingExtras => _extras;
    internal void NotifyDisplayPropertyChanged(string propertyName) => OnPropertyChanged(propertyName);

    /// <summary>
    /// Builds a row backed by an index record, holding the record rather than copying values out of it.
    /// </summary>
    internal static AppSearchResult FromIndexResult(Core.SearchResult item, string query, int index, bool isApplication, string? scope)
    {
        var row = new AppSearchResult
        {
            _source = item,
            ResultKind = isApplication ? "Application" : "File",
            Index = index,
            SearchQuery = query
        };
        // Left unallocated in the overwhelmingly common unscoped case -- only a scoped quick-window
        // search needs somewhere to remember the scope, and that is capped at a few dozen rows.
        if (!string.IsNullOrEmpty(scope))
            row.Extras.Scope = scope;
        return row;
    }

    // Every Scaled* property below reads UiMetrics live at get-time, but WPF only re-queries a binding
    // when ITS OWN PropertyChanged fires for that property -- an existing row never picks up a change
    // to UiMetrics.Scale on its own (a search's own newly-built rows do, since they're fresh objects
    // constructed after the change). Called from UiMetrics.ScaleChanged subscribers (e.g.
    // QuickSearchViewModel) so already-displayed rows resize live instead of only updating on the next
    // search. Empty property name means "every property on this object changed".
    public void RefreshScale() => OnPropertyChanged(string.Empty);

    public string Name
    {
        get
        {
            if (_extras?.Name is { } set) return set;
            if (_source is not { } s) return string.Empty;
            return string.IsNullOrWhiteSpace(s.Name) ? s.Path : s.Name;
        }
        set => Extras.Name = value;
    }

    public string FullPath
    {
        get => _extras?.FullPath ?? _source?.Path ?? string.Empty;
        set => Extras.FullPath = value;
    }

    /// <summary>
    /// The path text shown under the name. Derived from the source record rather than stored, then
    /// cached -- deriving it allocates a string, and a row the grid never realizes must not pay for one.
    /// </summary>
    public string ParentDir
    {
        get
        {
            if (_extras?.ParentDir is { } set) return set;
            if (_source is not { } s) return string.Empty;
            // Cached on the way out: several bindings on a realized row read this (the subtitle itself,
            // HasPathSubtitle, and the font size that depends on it), and re-deriving it per binding
            // would run GetDirectoryName over and over for one row.
            return Extras.ParentDir = SearchResultHelper.GetParentDisplayText(s, IsApplication, _extras?.Scope);
        }
        set => Extras.ParentDir = value;
    }

    public string ContextDirectory
    {
        get
        {
            if (_extras?.ContextDirectory is { } set) return set;
            if (_source is not { } s) return string.Empty;
            if (s.IsDir) return s.Path;
            return Extras.ContextDirectory = System.IO.Path.GetDirectoryName(s.Path) ?? s.Drive + ":\\";
        }
        set => Extras.ContextDirectory = value;
    }

    public bool IsDir
    {
        get => _extras?.IsDir ?? _source?.IsDir ?? false;
        set => Extras.IsDir = value;
    }

    public string Drive
    {
        get => _extras?.Drive ?? _source?.Drive ?? string.Empty;
        set => Extras.Drive = value;
    }

    public string ResultKind { get; set; } = "File";
    public int Index { get; set; }
    public string SearchQuery { get; set; } = string.Empty;
    public bool IsApplication => ResultKind == "Application";
    public bool IsPluginSearchAction => ResultKind == "PluginAction";
    public bool IsSearchSectionHeader => ResultKind == "SectionHeader";
    public bool IsJumpToExplorerPath => ResultKind == "JumpToExplorerPath";
    public bool IsEmptyResult => ResultKind == "Empty";
    public bool IsInstantResult => ResultKind == "InstantResult";
    public bool IsListItem => ResultKind == "ListItem";
    // Genuine on-disk file/folder and application results get the preview window — excludes
    // calculator/URL/env instant results, plugin actions, jump-to-path, list items, headers, empties,
    // and the "show more" row.
    public bool CanPreview => (ResultKind == "File" || ResultKind == "Application") && FullPath != "__SHOW_MORE__";
    // Mirrors the exact conditions DataTemplates.xaml's path-subtitle row collapses under (applications,
    // a blank ParentDir, or the "no results" placeholder) -- single source of truth for both that
    // visibility and the name-line font size below, so they can never drift out of sync with each other.
    public bool HasPathSubtitle => !IsApplication && ParentDir != "" && !IsEmptyResult;
    // A row must be at least as tall as its own icon plus the row border's own vertical margin (see
    // UiMetrics.ResultRowVerticalMargin) -- otherwise the icon can exceed the row and either get clipped
    // or force it to overflow past its allotted layout space. Section headers used to get their own
    // (shorter) SearchSectionHeaderHeight here, independent of what a normal row actually measures --
    // deliberately unified to the exact same height a normal row gets, so every row-height-sum
    // calculation that assumes a uniform row size (see InlineSearchWindowLayoutManager) can't drift from
    // what headers actually render at.
    public double ItemHeight => IsListItem ? UiMetrics.ListItemHeight : Math.Max(UiMetrics.SearchResultItemHeight, UiMetrics.ResultIconSize + UiMetrics.ResultRowVerticalMargin + UiMetrics.IconRowBreathingRoom);

    // Base visual metrics (inline/full windows use these — no scaling). A row with no path subtitle
    // gives its whole line-height budget to the name instead of splitting it with an empty second line.
    public double NameFontSize => HasPathSubtitle ? UiMetrics.ResultNameFontSize : UiMetrics.ResultNameFontSizeSingleLine;
    public double PathFontSize => UiMetrics.ResultPathFontSize;
    public double ResultIconSize => UiMetrics.ResultIconSize;

    // Scaled variants — bound only in the quick window, so it alone grows/shrinks with the
    // configured search box height. Section headers unified to the normal row height here too (see
    // ItemHeight's own comment).
    public double ScaledItemHeight => IsListItem ? UiMetrics.ScaledListItemHeight : UiMetrics.ScaledNormalRowHeight;
    public double ScaledNameFontSize => HasPathSubtitle ? UiMetrics.ScaledResultNameFontSize : UiMetrics.ScaledResultNameFontSizeSingleLine;
    public double ScaledPathFontSize => UiMetrics.ScaledResultPathFontSize;
    public double ScaledResultIconSize => UiMetrics.ScaledResultIconSize;
    // UiMetrics.InlineRowHeight is a literal design constant (not derived from ItemHeight/ResultIconSize),
    // so every non-list-item row is uniformly that height -- which InlineSearchWindowLayoutManager's own
    // height-sum relies on to land exactly on a whole multiple of the row height for every row count.
    public double InlineItemHeight => IsListItem ? ItemHeight : UiMetrics.InlineRowHeight;
    public double ActionsHeaderHeight => Math.Round(ItemHeight * 0.7);
    public string DisplayPath => IsApplication ? ParentDir : FullPath;

    public uint PluginActionId
    {
        get => _extras?.PluginActionId ?? 0;
        set => Extras.PluginActionId = value;
    }

    public string PluginActionArgumentText
    {
        get => _extras?.PluginActionArgumentText ?? string.Empty;
        set => Extras.PluginActionArgumentText = value;
    }

    public System.Windows.Media.ImageSource? IconOverride
    {
        get => _extras?.IconOverride;
        set => Extras.IconOverride = value;
    }

    public string InstantResultActionType
    {
        get => _extras?.InstantResultActionType ?? "Copy";
        set => Extras.InstantResultActionType = value;
    }

    public string InstantResultActionArgument
    {
        get => _extras?.InstantResultActionArgument ?? string.Empty;
        set => Extras.InstantResultActionArgument = value;
    }

    public Action? InstantResultOnExecute
    {
        get => _extras?.InstantResultOnExecute;
        set => Extras.InstantResultOnExecute = value;
    }

    public string? TabCompletion
    {
        get => _extras?.TabCompletion;
        set => Extras.TabCompletion = value;
    }

    public object? SourceProvider
    {
        get => _extras?.SourceProvider;
        set => Extras.SourceProvider = value;
    }

    public bool[]? GetHighlightMask(string text, string query) => AppSearchResultDisplaySupport.GetHighlightMask(this, text, query);

    // Visual properties

    public string IconData => AppSearchResultDisplaySupport.GetIconData(this);
    public System.Windows.Media.ImageSource? Icon => AppSearchResultDisplaySupport.GetIcon(this);

    public string ShortcutHint
    {
        get => _extras?.ShortcutHint ?? string.Empty;
        set
        {
            if (ShortcutHint != value)
            {
                Extras.ShortcutHint = value;
                OnPropertyChanged(nameof(ShortcutHint));
            }
        }
    }

    public Visibility ShortcutVisibility
    {
        get => _extras?.ShortcutVisibility ?? Visibility.Collapsed;
        set
        {
            if (ShortcutVisibility != value)
            {
                Extras.ShortcutVisibility = value;
                OnPropertyChanged(nameof(ShortcutVisibility));
            }
        }
    }

    // Already known from the index (Core.SearchResult.Metadata) for most results; DateTime.MinValue
    // (see FileMetadata) falls back below.
    public PluginSdk.Abstractions.FileMetadata Metadata
    {
        get => _extras?.Metadata ?? _source?.Metadata ?? default;
        set => Extras.Metadata = value;
    }

    // Lazy-loaded file modification time when the index did not provide it.
    public DateTime DateModified
    {
        get
        {
            if (_extras?.DateModified is { } cached)
                return cached;
            // Deliberately ahead of any Extras allocation: the index knows the date for almost every
            // result, and the date column sorts by reading this on every row. A row that can answer
            // from its own record must not have to allocate to do it.
            var known = Metadata.Modified;
            if (known != DateTime.MinValue)
                return (Extras.DateModified = known).Value;
            return AppSearchResultDisplaySupport.GetDateModified(this);
        }
    }

    public string DateModifiedText
    {
        get
        {
            var dt = DateModified;
            return dt == DateTime.MinValue ? TranslationManager.Instance["Model_TimeUnknown"] : dt.ToString("yyyy/MM/dd HH:mm");
        }
    }

    public string this[string columnId]
    {
        get => AppSearchResultDisplaySupport.GetColumnValue(this, columnId);
        set => AppSearchResultDisplaySupport.SetColumnValue(this, columnId, value);
    }
}
