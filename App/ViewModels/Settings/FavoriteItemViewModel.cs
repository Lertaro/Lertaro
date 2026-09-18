using System.Windows.Input;
using Lertaro.App.Helpers;

namespace Lertaro.App.ViewModels.Settings;

// Split out purely to keep FavoritesSettingsViewModel under the repository's per-file line limit; this
// class owns the state of one favorite row in the favorites list, including its global-hotkey field.
public class FavoriteItemViewModel : ViewModelBase
{
    private string _name = string.Empty;
    private string _path = string.Empty;
    private string _editName = string.Empty;
    private string _editPath = string.Empty;
    private string _hotkey = string.Empty;
    private string _hotkeyHint = string.Empty;
    private bool _isEditing;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string Path
    {
        get => _path;
        set
        {
            if (SetProperty(ref _path, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>
    /// This favorite's optional global hotkey, in the flat recorder format. Editing it drops whatever
    /// the last apply said about the old combination, which no longer describes what is configured.
    /// </summary>
    public string Hotkey
    {
        get => _hotkey;
        set
        {
            if (!SetProperty(ref _hotkey, value)) return;

            HotkeyHint = string.Empty;
        }
    }

    /// <summary>
    /// Why this row's hotkey did not take effect -- the combination is owned by another application, or
    /// an earlier favorite already claimed it. Empty means the row registered (or has no hotkey at all).
    /// </summary>
    public string HotkeyHint
    {
        get => _hotkeyHint;
        set => SetProperty(ref _hotkeyHint, value);
    }

    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value);
    }

    public string EditPath
    {
        get => _editPath;
        set
        {
            if (SetProperty(ref _editPath, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name))
                return Name;

            var expanded = FavoritePathResolver.Expand(Path);
            if (FavoritePathResolver.IsVirtualPath(expanded))
            {
                return PluginSdk.Helpers.ShellPathHelper.GetVirtualFolderDisplayName(expanded, Path);
            }
            if (FavoriteUrlHelper.IsWebUrl(Path))
            {
                return Path.Trim();
            }
            try
            {
                var name = System.IO.Path.GetFileName(expanded.TrimEnd('\\', '/'));
                return string.IsNullOrEmpty(name) ? Path : name;
            }
            catch { return Path; }
        }
    }
}
