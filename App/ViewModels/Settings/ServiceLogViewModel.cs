using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;

using Lertaro.Core.Services.Search;

using Lertaro.Core.SearchIndex;
using Lertaro.App.ViewModels.Settings.General;
namespace Lertaro.App.ViewModels.Settings;

// A single rendered log line, colored by its level for the log-view tabs on the Service Status page.
public sealed record LogLineViewModel(string Text, LogLevel Level);

// Reads and displays the App/Service/Hook processes' own log files (Core.Logger writes one plain-text
// file per process, no in-memory buffer) in a 3-tab view, polling for changes while open.
public class ServiceLogViewModel : ViewModelBase, IDisposable
{
    private const int MaxLines = 500;
    private readonly SearchService _searchService;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Dictionary<string, DateTime> _lastLoadedWriteTimes = new();
    private List<LogLineViewModel> _allLines = new();

    private string _selectedTab = "App";
    public string SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value))
            {
                Load(force: true);
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private bool _isServiceReady = true;
    public bool IsServiceReady
    {
        get => _isServiceReady;
        set
        {
            if (SetProperty(ref _isServiceReady, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private string _levelFilter = "All";
    public string LevelFilter
    {
        get => _levelFilter;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                // Same fix as the Network Drive refresh-mode combo (NetworkDriveSettingsItem.RefreshMode):
                // WPF's ComboBox transiently pushes SelectedValue=null through this TwoWay binding while
                // its ItemsSource item Labels are being mutated on a language change. Reject it and
                // re-raise so the combo re-syncs to the real value instead of latching onto the blank.
                OnPropertyChanged(nameof(LevelFilter));
                return;
            }
            if (SetProperty(ref _levelFilter, value))
                ApplyFilter();
        }
    }

    // Stable-identity, mutate-in-place options -- see LabeledOption's own doc comment. Never
    // reassigned/rebuilt; only .Label is updated in place on a language change (OnLanguageChanged).
    private readonly LabeledOption[] _levelFilterOptions;
    public IReadOnlyList<LabeledOption> LevelFilterOptions => _levelFilterOptions;

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter();
        }
    }

    private ICommand? _selectTabCommand;
    public ICommand SelectTabCommand => _selectTabCommand ??= new RelayCommand<string>(tab => SelectedTab = tab);

    private ICommand? _clearCommand;
    public ICommand ClearCommand => _clearCommand ??= new RelayCommand(() => _ = ClearAsync(), CanClear);

    // Clearing the Service tab needs a live pipe round trip to the service process -- disable the
    // button rather than let the user fire a clear that can only silently fail while it's unreachable.
    private bool CanClear() => SelectedTab != "Service" || IsServiceReady;

    public ObservableRangeCollection<LogLineViewModel> Lines { get; } = new();

    public ServiceLogViewModel(SearchService searchService)
    {
        _searchService = searchService;
        _levelFilterOptions =
        [
            new LabeledOption("All", TranslationManager.Instance["Service_LogFilter_All"]),
            new LabeledOption("Error", TranslationManager.Instance["LogLevel_Error"]),
            new LabeledOption("Warn", TranslationManager.Instance["LogLevel_Warn"]),
            new LabeledOption("Info", TranslationManager.Instance["LogLevel_Info"]),
            new LabeledOption("Debug", TranslationManager.Instance["LogLevel_Debug"]),
        ];

        Load(force: true);
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (s, e) => Load(force: false);
        _refreshTimer.Start();

        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Update labels in place -- the combo's ItemsSource is bound to this same stable array/
        // reference and never gets reassigned, so its SelectedValue is never disturbed.
        _levelFilterOptions[0].Label = TranslationManager.Instance["Service_LogFilter_All"];
        _levelFilterOptions[1].Label = TranslationManager.Instance["LogLevel_Error"];
        _levelFilterOptions[2].Label = TranslationManager.Instance["LogLevel_Warn"];
        _levelFilterOptions[3].Label = TranslationManager.Instance["LogLevel_Info"];
        _levelFilterOptions[4].Label = TranslationManager.Instance["LogLevel_Debug"];
    }

    private string CurrentLogPath => SelectedTab switch
    {
        "Service" => Path.Combine(Logger.SharedDataDir, "logs", "service.log"),
        "Hook" => Path.Combine(Logger.UserDataDir, "logs", "hook.log"),
        _ => Path.Combine(Logger.UserDataDir, "logs", "app.log"),
    };

    private void Load(bool force)
    {
        try
        {
            var path = CurrentLogPath;
            if (!File.Exists(path))
            {
                if (force) { _allLines = new List<LogLineViewModel>(); ApplyFilter(); }
                return;
            }

            // Skip the (relatively expensive) re-read if the file hasn't changed since we last loaded
            // this tab -- the timer polls every 2s regardless of whether anything was actually written.
            var lastWrite = File.GetLastWriteTimeUtc(path);
            if (!force && _lastLoadedWriteTimes.TryGetValue(SelectedTab, out var previous) && previous == lastWrite)
                return;
            _lastLoadedWriteTimes[SelectedTab] = lastWrite;

            _allLines = File.ReadLines(path).TakeLast(MaxLines).Select(ParseLine).ToList();
            ApplyFilter();
        }
        catch
        {
            // Log file locked/missing/inaccessible -- leave the last successfully loaded content as-is.
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<LogLineViewModel> filtered = _allLines;
        if (LevelFilter != "All")
            filtered = filtered.Where(l => l.Level.ToString() == LevelFilter);
        if (!string.IsNullOrWhiteSpace(SearchText))
            // FuzzyMatcher.IsMatch splits a multi-word SearchText into independently-required terms
            // (same as Core's real file search) -- a plain .Contains(SearchText) treated the whole
            // typed text as one literal string, so e.g. "error timeout" would never match a line
            // containing both words non-contiguously.
            filtered = filtered.Where(l => FuzzyMatcher.IsMatch(SearchText, l.Text));
        Lines.ReplaceRange(filtered);
    }

    private static LogLineViewModel ParseLine(string raw)
    {
        var level = raw.Contains("[Error]") ? LogLevel.Error
            : raw.Contains("[Warn]") ? LogLevel.Warn
            : raw.Contains("[Debug]") ? LogLevel.Debug
            : LogLevel.Info;
        return new LogLineViewModel(raw, level);
    }

    private async Task ClearAsync()
    {
        try
        {
            if (SelectedTab == "Service")
            {
                // service.log lives under the service's own (elevated/system) data directory -- the App
                // process has no permission to truncate it directly, so ask the service to do it instead.
                await _searchService.ClearServiceLogAsync();
            }
            else
            {
                var path = CurrentLogPath;
                if (File.Exists(path))
                    File.WriteAllText(path, string.Empty);
            }

            _allLines = new List<LogLineViewModel>();
            ApplyFilter();
            _lastLoadedWriteTimes.Remove(SelectedTab);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
    }
}
