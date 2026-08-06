using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;

using Lertaro.Core.Wire;
namespace Lertaro.App.ViewModels.Settings.General;

public class GeneralSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private LogLevelOption? _selectedLogLevel;
    private IReadOnlyList<LogLevelOption>? _logLevelOptions;
    private IReadOnlyList<LanguageOption>? _languageOptions;

    // Staged edits -- everything below except SelectedTheme/PreferredLanguage (which apply live for
    // instant preview) only commits to _userSettings when Apply() runs (Settings window's Apply/OK).
    private bool _startWithWindows;
    private bool _autoCheckUpdates;
    private bool _autoSilentUpdate;
    private bool _enableHardwareAcceleration;
    private bool _enableFuzzyMatch;
    private bool _hideTrayIcon;
    private bool _defaultFileManagerEnabled;
    private string _defaultFileManagerPath;
    private string _defaultFileManagerParameter;
    private string _globalTokenPrefix;

    // Tab navigation for the System/Layout/Preview Window split of this page.
    private string _selectedTab = "System";
    public string SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    private ICommand? _selectTabCommand;
    public ICommand SelectTabCommand => _selectTabCommand ??= new RelayCommand<string>(tab => SelectedTab = tab);

    public GeneralSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        Layout = new SearchBarLayoutSettingsViewModel(userSettings);
        PreviewWindow = new PreviewWindowSettingsViewModel(userSettings);
        MainWindow = new MainWindowSettingsViewModel(userSettings);
        QuickNavigationOrder = new QuickNavigationOrderViewModel(userSettings);
        ResultTypeOrder = new ResultTypeOrderViewModel(userSettings);
        SidebarGroupOrder = new SidebarGroupOrderViewModel(userSettings);
        ColumnOrder = new ColumnOrderViewModel(userSettings);
        ActionMenuGroupOrder = new ActionMenuGroupOrderViewModel(userSettings);
        FilePreviewProviderOrder = new FilePreviewProviderOrderViewModel(userSettings);
        ThumbnailProviderOrder = new ThumbnailProviderOrderViewModel(userSettings);

        _startWithWindows = userSettings.StartWithWindows;
        _autoCheckUpdates = userSettings.AutoCheckUpdates;
        _autoSilentUpdate = userSettings.AutoSilentUpdate;
        _enableHardwareAcceleration = userSettings.EnableHardwareAcceleration;
        _enableFuzzyMatch = userSettings.EnableFuzzyMatch;
        _hideTrayIcon = userSettings.HideTrayIcon;
        _defaultFileManagerEnabled = userSettings.DefaultFileManager.Enabled;
        _defaultFileManagerPath = userSettings.DefaultFileManager.Path;
        _defaultFileManagerParameter = userSettings.DefaultFileManager.Parameter;
        _globalTokenPrefix = userSettings.GlobalTokenPrefix;

        _selectedLogLevel = LogLevelOptions.FirstOrDefault(o => o.Value == SettingsOptionGenerator.NormalizeLogLevel(_userSettings.LogLevel))
                            ?? LogLevelOptions[2]; // Default to Info

        // Dynamically refresh properties when the language changes
        TranslationManager.Instance.PropertyChanged += (s, e) =>
        {
            _logLevelOptions = null;
            _languageOptions = null;

            OnPropertyChanged(nameof(LogLevelOptions));
            OnPropertyChanged(nameof(LanguageOptions));

            // Let WPF bind the new ItemsSource first, then restore selections
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                var newLogLevel = LogLevelOptions.FirstOrDefault(o => o.Value == SettingsOptionGenerator.NormalizeLogLevel(_userSettings.LogLevel));
                if (newLogLevel != null)
                {
                    SelectedLogLevel = newLogLevel;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }

    // Persistence and the side effects (Logger.MinimumLevel, hook-process notification) are staged
    // until Apply() -- see the class-level comment.
    public LogLevelOption? SelectedLogLevel
    {
        get => _selectedLogLevel;
        set
        {
            if (value == null) return;
            if (_selectedLogLevel != value)
            {
                _selectedLogLevel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LogLevel));
            }
        }
    }

    public IReadOnlyList<LogLevelOption> LogLevelOptions
    {
        get
        {
            if (_logLevelOptions == null)
            {
                _logLevelOptions = SettingsOptionGenerator.GetLogLevelOptions();
            }
            return _logLevelOptions;
        }
    }

    public IReadOnlyList<LanguageOption> LanguageOptions
    {
        get
        {
            if (_languageOptions == null)
            {
                _languageOptions = SettingsOptionGenerator.GetLanguageOptions();
            }
            return _languageOptions;
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public bool AutoCheckUpdates
    {
        get => _autoCheckUpdates;
        set { if (SetProperty(ref _autoCheckUpdates, value)) OnPropertyChanged(nameof(IsAutoSilentUpdateEnabled)); }
    }

    public bool IsUserAdmin => ElevationHelper.IsUserAdmin();

    public bool IsAutoSilentUpdateEnabled => IsUserAdmin && AutoCheckUpdates;

    public bool AutoSilentUpdate
    {
        get => IsUserAdmin && _autoSilentUpdate;
        set { if (!IsUserAdmin) return; SetProperty(ref _autoSilentUpdate, value); }
    }

    public bool EnableHardwareAcceleration
    {
        get => _enableHardwareAcceleration;
        set => SetProperty(ref _enableHardwareAcceleration, value);
    }

    // Off narrows every bare query term from a subsequence match to a contiguous substring one, so
    // "abc" stops matching "a-b-c". Applies to the search itself, not just the ordering.
    public bool EnableFuzzyMatch
    {
        get => _enableFuzzyMatch;
        set => SetProperty(ref _enableFuzzyMatch, value);
    }

    public bool HideTrayIcon
    {
        get => _hideTrayIcon;
        set => SetProperty(ref _hideTrayIcon, value);
    }

    public string GlobalTokenPrefix
    {
        get => _globalTokenPrefix;
        set
        {
            var val = value ?? ":";
            if (val.Length > 1) val = val[..1];
            SetProperty(ref _globalTokenPrefix, val);
        }
    }

    // See GitHub issue #180 -- redirects "open a folder" (and "open containing folder") to a
    // user-configured third-party file manager instead of the shell's own association.
    public bool DefaultFileManagerEnabled
    {
        get => _defaultFileManagerEnabled;
        set => SetProperty(ref _defaultFileManagerEnabled, value);
    }

    public string DefaultFileManagerPath
    {
        get => _defaultFileManagerPath;
        set => SetProperty(ref _defaultFileManagerPath, value);
    }

    public string DefaultFileManagerParameter
    {
        get => _defaultFileManagerParameter;
        set => SetProperty(ref _defaultFileManagerParameter, value);
    }

    private ICommand? _browseDefaultFileManagerPathCommand;
    public ICommand BrowseDefaultFileManagerPathCommand => _browseDefaultFileManagerPathCommand ??= new RelayCommand(BrowseDefaultFileManagerPath);

    private void BrowseDefaultFileManagerPath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = $"{TranslationManager.Instance["General_DefaultFileManagerBrowseFilter"]}|*.exe" };
        if (dialog.ShowDialog() == true)
            DefaultFileManagerPath = dialog.FileName;
    }

    public string LogLevel => SettingsOptionGenerator.NormalizeLogLevel(_selectedLogLevel?.Value ?? _userSettings.LogLevel);

    public string PreferredLanguage
    {
        get => _userSettings.PreferredLanguage;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                OnPropertyChanged();
                return;
            }
            if (_userSettings.PreferredLanguage != value)
            {
                _userSettings.PreferredLanguage = value;
                _userSettings.Save();
                TranslationManager.Instance.CurrentCulture = value;
                OnPropertyChanged();
            }
        }
    }

    public void Apply()
    {
        var logLevelChanged = _userSettings.LogLevel != LogLevel;

        _userSettings.StartWithWindows = _startWithWindows;
        _userSettings.AutoCheckUpdates = _autoCheckUpdates;
        if (IsUserAdmin)
            _userSettings.AutoSilentUpdate = _autoSilentUpdate;
        _userSettings.EnableHardwareAcceleration = _enableHardwareAcceleration;
        _userSettings.EnableFuzzyMatch = _enableFuzzyMatch;
        // Push it straight into the process-wide default too, so toggling this takes effect for the
        // plugin catalog, favorites and highlighting immediately rather than only after a restart.
        SearchContext.DefaultFuzzyMatchEnabled = _enableFuzzyMatch;
        _userSettings.HideTrayIcon = _hideTrayIcon;
        _userSettings.GlobalTokenPrefix = string.IsNullOrWhiteSpace(_globalTokenPrefix) ? ":" : _globalTokenPrefix;
        _userSettings.LogLevel = LogLevel;
        _userSettings.DefaultFileManager.Enabled = _defaultFileManagerEnabled;
        _userSettings.DefaultFileManager.Path = _defaultFileManagerPath;
        _userSettings.DefaultFileManager.Parameter = _defaultFileManagerParameter;

        StartupManager.SetEnabled(StartWithWindows);
        (System.Windows.Application.Current.MainWindow as QuickSearchWindow)?.ApplyTrayIconVisibility(_hideTrayIcon);
        Logger.MinimumLevel = SettingsOptionGenerator.ParseLogLevel(LogLevel);
        if (logLevelChanged)
        {
            // Propagate to hook process so hook.log also respects the new level
            App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.ReloadSettings });
        }

        Layout.Save();
        PreviewWindow.Save();
        MainWindow.Save();
        QuickNavigationOrder.Save();
        ResultTypeOrder.Save();
        SidebarGroupOrder.Save();
        ColumnOrder.Save();
        ActionMenuGroupOrder.Save();
        FilePreviewProviderOrder.Save();
        ThumbnailProviderOrder.Save();

        _userSettings.Save();
    }

    public SearchBarLayoutSettingsViewModel Layout { get; }
    public PreviewWindowSettingsViewModel PreviewWindow { get; }
    public MainWindowSettingsViewModel MainWindow { get; }
    public QuickNavigationOrderViewModel QuickNavigationOrder { get; }
    public ResultTypeOrderViewModel ResultTypeOrder { get; }
    public SidebarGroupOrderViewModel SidebarGroupOrder { get; }
    public ColumnOrderViewModel ColumnOrder { get; }
    public ActionMenuGroupOrderViewModel ActionMenuGroupOrder { get; }
    public FilePreviewProviderOrderViewModel FilePreviewProviderOrder { get; }
    public ThumbnailProviderOrderViewModel ThumbnailProviderOrder { get; }
}
