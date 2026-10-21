using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;
namespace Lertaro.App.ViewModels.Settings.General;

// Root of the General settings page. Canonical staging rule for the page and every sub-ViewModel it
// owns: edits change local fields only and commit to _userSettings/UiMetrics when Save() runs, and
// Save() only runs from Apply() below (the Settings window's Apply/OK). Each sub-VM header names its key.
public class GeneralSettingsViewModel : ViewModelBase
{
    private readonly System.ComponentModel.PropertyChangedEventHandler _translationHandler;

    private readonly UserSettings _userSettings;
    private LogLevelOption? _selectedLogLevel;
    private IReadOnlyList<LogLevelOption>? _logLevelOptions;
    private IReadOnlyList<LanguageOption>? _languageOptions;

    // Staged per the class-level rule, except PreferredLanguage further down, which writes straight
    // through to _userSettings so the UI retranslates live.
    private bool _startWithWindows;
    private bool _autoCheckUpdates;
    private bool _autoSilentUpdate;
    private bool _enableHardwareAcceleration;
    private bool _enableFuzzyMatch;
    private bool _orFirstPrecedence;
    private bool _enableQuickSearchClipboardAutoFill;
    private bool _keepSearchText;
    private bool _enableEverythingIpc;
    private bool _enableRankingEventLog;
    private bool _showOpenedFoldersInInlineSearch;
    private int _inlineSearchWindowWidthPercent;
    private bool _hideTrayIcon;
    private bool _openFoldersInNewExplorerTabs;
    private string _globalTokenPrefix;

    // Tab navigation across this page's six tabs (System/Layout/SearchWindow/PreviewWindow/
    // QuickNavigation/PreviewProviders).
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
        FileManager = new DefaultFileManagerSettingsViewModel(userSettings);
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
        _orFirstPrecedence = userSettings.OrFirstPrecedence;
        _enableQuickSearchClipboardAutoFill = userSettings.EnableQuickSearchClipboardAutoFill;
        _keepSearchText = userSettings.SearchWindow.KeepSearchText;
        _enableEverythingIpc = userSettings.EnableEverythingIpc;
        _enableRankingEventLog = userSettings.EnableRankingEventLog;
        _showOpenedFoldersInInlineSearch = userSettings.ShowOpenedFoldersInInlineSearch;
        _inlineSearchWindowWidthPercent = userSettings.InlineSearchWindowWidthPercent ?? UiMetrics.DefaultInlineCardWidthPercent;
        _hideTrayIcon = userSettings.HideTrayIcon;
        _openFoldersInNewExplorerTabs = userSettings.DefaultFileManager.OpenFoldersInNewExplorerTabs;
        _globalTokenPrefix = userSettings.GlobalTokenPrefix;

        _selectedLogLevel = LogLevelOptions.FirstOrDefault(o => o.Value == SettingsOptionGenerator.NormalizeLogLevel(_userSettings.LogLevel))
                            ?? LogLevelOptions[2]; // Default to Info

        // Dynamically refresh properties when the language changes
        _translationHandler = (s, e) =>
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
        TranslationManager.Instance.PropertyChanged += _translationHandler;
    }

    // Its side effects (Logger.MinimumLevel, hook-process notification) are staged too -- see the
    // class-level rule.
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

    // How spaces (AND) and the pipe '|' (OR) bind when a query mixes them. Off is AND-first, the
    // default: "report | summary 2024" means report OR (summary AND 2024). On restores the historical
    // OR-first reading of the same query -- (report OR summary) AND 2024.
    public bool OrFirstPrecedence
    {
        get => _orFirstPrecedence;
        set => SetProperty(ref _orFirstPrecedence, value);
    }

    public bool EnableQuickSearchClipboardAutoFill { get => _enableQuickSearchClipboardAutoFill; set => SetProperty(ref _enableQuickSearchClipboardAutoFill, value); }

    public bool EnableEverythingIpc
    {
        get => _enableEverythingIpc;
        set => SetProperty(ref _enableEverythingIpc, value);
    }

    public bool EnableRankingEventLog
    {
        get => _enableRankingEventLog;
        set => SetProperty(ref _enableRankingEventLog, value);
    }

    public bool ShowOpenedFoldersInInlineSearch
    {
        get => _showOpenedFoldersInInlineSearch;
        set => SetProperty(ref _showOpenedFoldersInInlineSearch, value);
    }

    /// <summary>How wide the inline card may get, as a percent of the window it docks to.</summary>
    /// <remarks>
    /// The box takes 20-100, but the number is a request rather than a size: the placement bounds the result
    /// by the anchored window and its screen, so 40 over a maximized window and 40 over a 400px dialog come
    /// out different widths. Saving the default back stores null, not 40 -- see GeneralSettingsApplier --
    /// because null keeps the floor an untouched card has always had on a small window.
    /// </remarks>
    public int InlineSearchWindowWidthPercent
    {
        get => _inlineSearchWindowWidthPercent;
        set
        {
            if (value < UiMetrics.MinInlineCardWidthPercent || value > UiMetrics.MaxInlineCardWidthPercent)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"Width must be between {UiMetrics.MinInlineCardWidthPercent} and {UiMetrics.MaxInlineCardWidthPercent} percent.");
            }
            SetProperty(ref _inlineSearchWindowWidthPercent, value);
        }
    }

    public bool HideTrayIcon
    {
        get => _hideTrayIcon;
        set => SetProperty(ref _hideTrayIcon, value);
    }

    public bool OpenFoldersInNewExplorerTabs
    {
        get => _openFoldersInNewExplorerTabs;
        set => SetProperty(ref _openFoldersInNewExplorerTabs, value);
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

    // Governs both windows: the quick one keeps its box on hide, the full one stops emptying it on Escape.
    // Stored under SearchWindow settings; the row lives on the System tab because it is not a per-window
    // layout option any more.
    public bool KeepSearchText
    {
        get => _keepSearchText;
        set => SetProperty(ref _keepSearchText, value);
    }

    public void Apply() => GeneralSettingsApplier.Apply(
        this,
        _userSettings,
        _startWithWindows,
        _autoCheckUpdates,
        _autoSilentUpdate,
        _enableHardwareAcceleration,
        _enableFuzzyMatch,
        _orFirstPrecedence,
        _enableQuickSearchClipboardAutoFill,
        _keepSearchText,
        _enableEverythingIpc,
        _enableRankingEventLog,
        _showOpenedFoldersInInlineSearch,
        _inlineSearchWindowWidthPercent,
        _hideTrayIcon,
        _openFoldersInNewExplorerTabs,
        _globalTokenPrefix,
        LogLevel);

    public SearchBarLayoutSettingsViewModel Layout { get; }
    public PreviewWindowSettingsViewModel PreviewWindow { get; }
    public MainWindowSettingsViewModel MainWindow { get; }
    public DefaultFileManagerSettingsViewModel FileManager { get; }
    public QuickNavigationOrderViewModel QuickNavigationOrder { get; }
    public ResultTypeOrderViewModel ResultTypeOrder { get; }
    public SidebarGroupOrderViewModel SidebarGroupOrder { get; }
    public ColumnOrderViewModel ColumnOrder { get; }
    public ActionMenuGroupOrderViewModel ActionMenuGroupOrder { get; }
    public FilePreviewProviderOrderViewModel FilePreviewProviderOrder { get; }
    public ThumbnailProviderOrderViewModel ThumbnailProviderOrder { get; }

    public void Cleanup()
    {
        QuickNavigationOrder.Cleanup();
        ResultTypeOrder.Cleanup();
        SidebarGroupOrder.Cleanup();
        ColumnOrder.Cleanup();
        ActionMenuGroupOrder.Cleanup();
        FilePreviewProviderOrder.Cleanup();
        ThumbnailProviderOrder.Cleanup();
        TranslationManager.Instance.PropertyChanged -= _translationHandler;
    }
}
