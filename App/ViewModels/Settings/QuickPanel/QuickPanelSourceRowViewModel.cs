using Lertaro.App.Services;
using Lertaro.Core;
namespace Lertaro.App.ViewModels.Settings.QuickPanel;

/// <summary>One entry of the kind dropdown: the value that is stored, and the label that is shown.</summary>
public sealed record QuickPanelSourceKindOption(QuickPanelSourceKind Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// One row of a tab's source list, which is also one group in the panel. Folders are the only kind of
/// source today; anything else (favorites, the system's own recent list) will arrive as a plugin and
/// register its own id alongside these, so what the list offers -- rename, show/hide, reorder -- is
/// deliberately expressed against a source id rather than against a folder.
/// </summary>
public class QuickPanelSourceRowViewModel : ViewModelBase
{
    private readonly QuickPanelFolderSource? _folder;

    private QuickPanelSourceRowViewModel(string id, string defaultName, QuickPanelFolderSource? folder)
    {
        Id = id;
        DefaultName = defaultName;
        _folder = folder;
    }

    public static QuickPanelSourceRowViewModel ForFolder(QuickPanelFolderSource folder)
        => new(folder.Id, QuickPanelFolderSource.DefaultName(folder.Path), folder)
        {
            _path = folder.Path,
            _kind = folder.Kind,
            _sortByModified = folder.SortByModified || folder.Kind == QuickPanelSourceKind.AllByModified,
            _recursive = folder.Recursive,
            _filterPattern = folder.FilterPattern,
            _maxItems = folder.MaxItems,
            _maxAgeMinutes = folder.MaxAgeMinutes,
            _acceptsDrops = folder.AcceptsDrops,
        };

    /// <summary>A row for a plugin-provided source, which has none of a folder's fields.</summary>
    /// <remarks>
    /// The list holds both kinds because everything it offers -- rename, show/hide, reorder -- applies to
    /// both, and the settings store them under one id space. What differs is real rather than cosmetic:
    /// a plugin decides for itself what it returns, so there is no path, no "show" dropdown, no filter,
    /// no cap and no drop target. IsFolderSource is what the row template hides those behind.
    /// </remarks>
    public static QuickPanelSourceRowViewModel ForPlugin(string componentId, string name)
        => new(componentId, name, folder: null);

    /// <summary>Whether this row is a folder the user picked, as opposed to a plugin's own source.</summary>
    public bool IsFolderSource => _folder != null;

    public string Id { get; }

    /// <summary>What the group is called when the user has not renamed it.</summary>
    public string DefaultName { get; }

    private string _displayName = string.Empty;

    /// <summary>The user's own name for this group. Empty means fall back to <see cref="DefaultName"/>.</summary>
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value))
                OnPropertyChanged(nameof(EffectiveName));
        }
    }

    /// <summary>What the row heading shows, and what the panel will show.</summary>
    public string EffectiveName => string.IsNullOrWhiteSpace(DisplayName) ? DefaultName : DisplayName.Trim();

    private bool _isVisible = true;

    /// <summary>Unchecked hides the group from the panel without deleting the source.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    private bool _acceptsDrops;

    /// <summary>
    /// Whether this group takes files dragged onto it, copying them into its folder. Off unless asked
    /// for: a panel that quietly wrote into whatever folder the pointer happened to be over would be a
    /// worse thing to get wrong than one that ignores a drop.
    /// </summary>
    public bool AcceptsDrops
    {
        get => _acceptsDrops;
        set => SetProperty(ref _acceptsDrops, value);
    }

    private bool _showAsList;

    /// <summary>
    /// Whether this group opens as the detail list rather than as thumbnails. Stored inverted, as the
    /// preference's <see cref="QuickPanelGroupPreference.ThumbnailView"/>: tiles are what a group starts
    /// as, so the box that is worth checking is the one that turns them off. A folder of documents wants
    /// names and dates and a folder of images wants tiles, and which is which is a property of the
    /// folder -- the panel's own toggle overrides this for the session it is pressed in.
    /// </summary>
    public bool ShowAsList
    {
        get => _showAsList;
        set => SetProperty(ref _showAsList, value);
    }

    private bool _isExpanded;

    /// <summary>Whether this row's advanced block is open. Purely a settings-page state, never stored.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>
    /// Opens and closes the block above. A command driving a plain Button rather than a ToggleButton
    /// bound to <see cref="IsExpanded"/>: a ToggleButton's checked state paints the system accent
    /// colour straight over whatever style it is given, which in this app's dark settings window is a
    /// bright blue block where an icon should be.
    /// </summary>
    public System.Windows.Input.ICommand ToggleOptionsCommand => _toggleOptions ??= new Helpers.RelayCommand(() => IsExpanded = !IsExpanded);

    private System.Windows.Input.ICommand? _toggleOptions;

    private string _path = string.Empty;
    public string Path
    {
        get => _path;
        set
        {
            if (SetProperty(ref _path, value))
                OnPropertyChanged(nameof(EffectiveName));
        }
    }

    /// <summary>Picks this source's folder, starting from wherever it currently points.</summary>
    /// <remarks>
    /// The box stays editable beside it: a path can be typed or pasted faster than it can be clicked to,
    /// and a network share nobody has mapped is reachable only that way. This is the shortcut, not the
    /// only way in.
    /// </remarks>
    public System.Windows.Input.ICommand BrowseCommand => _browse ??= new Helpers.RelayCommand(Browse);

    private System.Windows.Input.ICommand? _browse;

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        // Spelled out: this type's own Path property shadows System.IO.Path inside it.
        if (!string.IsNullOrWhiteSpace(Path) && System.IO.Directory.Exists(Path))
            dialog.InitialDirectory = Path;

        if (dialog.ShowDialog() == true)
            Path = dialog.FolderName;
    }

    /// <summary>
    /// What the kind dropdown offers, as value+label pairs rather than items with translated Content
    /// and the enum in Tag. That is how every other dropdown in Settings is built, for two reasons this
    /// one hit both of: the label is only ever a label, so switching language rebuilds the list without
    /// touching the selection, and the selection matches on the value rather than on identity.
    /// </summary>
    public IReadOnlyList<QuickPanelSourceKindOption> KindOptions => _kindOptions ??= BuildKindOptions();

    private IReadOnlyList<QuickPanelSourceKindOption>? _kindOptions;

    private static IReadOnlyList<QuickPanelSourceKindOption> BuildKindOptions() => new[]
    {
        new QuickPanelSourceKindOption(QuickPanelSourceKind.RecentFiles, TranslationManager.Instance["QuickPanel_KindRecentFiles"]),
        new QuickPanelSourceKindOption(QuickPanelSourceKind.All, TranslationManager.Instance["QuickPanel_KindAll"]),
        new QuickPanelSourceKindOption(QuickPanelSourceKind.FoldersOnly, TranslationManager.Instance["QuickPanel_KindFoldersOnly"]),
        new QuickPanelSourceKindOption(QuickPanelSourceKind.FilesOnly, TranslationManager.Instance["QuickPanel_KindFilesOnly"]),
    };

    /// <summary>Rebuilds the labels after a language switch, keeping the selected value.</summary>
    public void RefreshTranslations()
    {
        _kindOptions = null;
        OnPropertyChanged(nameof(KindOptions));
        OnPropertyChanged(nameof(Kind));
    }

    private QuickPanelSourceKind _kind = QuickPanelSourceKind.RecentFiles;
    public QuickPanelSourceKind Kind
    {
        get => _kind;
        set
        {
            if (SetProperty(ref _kind, value))
                OnPropertyChanged(nameof(IsRecentFiles));
        }
    }

    /// <summary>The age limit only means anything for a recent-files source, so the field hides otherwise.</summary>
    public bool IsRecentFiles => Kind == QuickPanelSourceKind.RecentFiles;

    private bool _sortByModified;
    public bool SortByModified
    {
        get => _sortByModified;
        set => SetProperty(ref _sortByModified, value);
    }

    private bool _recursive;
    public bool Recursive
    {
        get => _recursive;
        set => SetProperty(ref _recursive, value);
    }

    private string _filterPattern = string.Empty;
    public string FilterPattern
    {
        get => _filterPattern;
        set => SetProperty(ref _filterPattern, value);
    }

    private int _maxItems = 20;

    /// <summary>0 means everything the source has; the upper bound keeps one group from filling the panel.</summary>
    public int MaxItems
    {
        get => _maxItems;
        set
        {
            if (value < 0 || value > 200)
                throw new ArgumentOutOfRangeException(nameof(value), "Count must be between 0 and 200.");
            SetProperty(ref _maxItems, value);
        }
    }

    private int _maxAgeMinutes;

    /// <summary>0 means no age limit. Same 30-day ceiling the Startup Panel's own field uses.</summary>
    public int MaxAgeMinutes
    {
        get => _maxAgeMinutes;
        set
        {
            if (value < 0 || value > 43200)
                throw new ArgumentOutOfRangeException(nameof(value), "Time range must be between 0 and 43200 minutes.");
            SetProperty(ref _maxAgeMinutes, value);
        }
    }

    /// <summary>Writes this row's folder fields back into the source it came from. Built-ins have none.</summary>
    public void SaveFolderFields()
    {
        if (_folder == null)
            return;
        _folder.Path = Path.Trim().Trim('"');
        _folder.Kind = Kind;
        _folder.SortByModified = SortByModified;
        _folder.Recursive = Recursive;
        _folder.FilterPattern = FilterPattern.Trim();
        _folder.MaxItems = MaxItems;
        _folder.MaxAgeMinutes = MaxAgeMinutes;
        _folder.AcceptsDrops = AcceptsDrops;
    }
}
