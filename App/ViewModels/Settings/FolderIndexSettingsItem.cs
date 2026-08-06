using System.Windows.Input;
using Lertaro.App.Services;

using Lertaro.App.ViewModels.Settings.NetworkDrive;
namespace Lertaro.App.ViewModels.Settings;

public class FolderIndexSettingsItem : ViewModelBase, INetworkRowItem
{
    private bool _isEnabled;
    private string _refreshMode = "Manual";
    private string _state = string.Empty;
    private string _itemCount = string.Empty;
    private bool _canRunRowAction;
    private bool _canEditEnabled;
    private bool _canEditRefreshMode;
    private bool _isPresent = true;
    private NetworkDriveRowAction _rowAction;

    // The path itself is the identity/key -- no separate Id, unlike a drive letter which needs a stable
    // volume-id to resolve.
    public string Path { get; set; } = string.Empty;
    public ICommand RowActionCommand { get; set; } = null!;
    public bool AppliedEnabled { get; set; }

    // GetFileName returns "" for a bare UNC share root ("\\server\share") -- .NET's GetPathRoot treats
    // the share itself as its own root (root.Length == path.Length), leaving nothing for GetFileName to
    // peel off, unlike a real subfolder ("\\server\share\Sub" -> "Sub"). Manually take the last path
    // segment in that case, so a share-root entry shows just "share" like any other row instead of the
    // whole UNC string; only falls back to the full path (mirrors HistorySettingsViewModel.MapSearchEntry)
    // if there's truly no segment to extract, so the row always shows something instead of going blank.
    public string DisplayName
    {
        get
        {
            var trimmed = Path.TrimEnd('\\', '/');
            var name = System.IO.Path.GetFileName(trimmed);
            if (!string.IsNullOrEmpty(name))
                return name;

            var withoutLeadingSeparators = trimmed.TrimStart('\\', '/');
            var sepIndex = withoutLeadingSeparators.LastIndexOfAny(new[] { '\\', '/' });
            var lastSegment = sepIndex >= 0 ? withoutLeadingSeparators.Substring(sepIndex + 1) : withoutLeadingSeparators;
            return string.IsNullOrEmpty(lastSegment) ? trimmed : lastSegment;
        }
    }

    public string State { get => _state; set => SetProperty(ref _state, value); }
    public string ItemCount { get => _itemCount; set => SetProperty(ref _itemCount, value); }
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    public bool IsPresent { get => _isPresent; set => SetProperty(ref _isPresent, value); }
    public bool CanEditEnabled { get => _canEditEnabled; set => SetProperty(ref _canEditEnabled, value); }
    public bool CanEditRefreshMode { get => _canEditRefreshMode; set => SetProperty(ref _canEditRefreshMode, value); }

    public string RefreshMode
    {
        get => _refreshMode;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                // WPF's ComboBox transiently pushes SelectedValue=null through this TwoWay binding
                // while its ItemsSource is being rebuilt (e.g. RefreshModeOptions refreshing labels
                // on a language change). Reject it and re-raise so the combo re-syncs to the real value.
                OnPropertyChanged(nameof(RefreshMode));
                return;
            }
            if (SetProperty(ref _refreshMode, value))
                OnPropertyChanged(nameof(RefreshModeText));
        }
    }

    public string RefreshModeText => RefreshMode switch
    {
        "Manual" => TranslationManager.Instance["Network_ModeManual"],
        "15Minutes" => TranslationManager.Instance["Network_Mode15M"],
        "Hourly" => TranslationManager.Instance["Network_ModeHourly"],
        "Daily" => TranslationManager.Instance["Network_ModeDaily"],
        _ => RefreshMode
    };

    public bool CanRunRowAction
    {
        get => _canRunRowAction;
        set
        {
            if (SetProperty(ref _canRunRowAction, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public NetworkDriveRowAction RowAction
    {
        get => _rowAction;
        set
        {
            if (SetProperty(ref _rowAction, value))
            {
                OnPropertyChanged(nameof(IsRowActionVisible));
                OnPropertyChanged(nameof(RowActionText));
            }
        }
    }

    public bool IsRowActionVisible => RowAction != NetworkDriveRowAction.None;
    public string RowActionText => RowAction switch
    {
        NetworkDriveRowAction.Rebuild => TranslationManager.Instance["Network_RowRebuildBtn"],
        NetworkDriveRowAction.Delete => TranslationManager.Instance["Network_RowDeleteBtn"],
        NetworkDriveRowAction.Stop => TranslationManager.Instance["Network_RowStopBtn"],
        _ => string.Empty
    };

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(RefreshModeText));
        OnPropertyChanged(nameof(RowActionText));
    }
}
