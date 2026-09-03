using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.Core;
using Lertaro.App.ViewModels.Service;

using Lertaro.Core.Services.Search;

namespace Lertaro.App.ViewModels.Search;

public class QuickSearchViewModel : ViewModelBase, IDisposable
{
    private readonly SearchService _searchService;
    private readonly QuickSearchLaunchSourceSupport _launchSources;

    public QuickSearchViewModel()
    {
        // Scale result rows/fonts/icons proportionally to the configured search box height.
        Services.UiMetrics.ApplyScaleFromSettings();

        _searchService = new SearchService();
        _launchSources = new QuickSearchLaunchSourceSupport(OnLaunchSourceChanged);
        Search = new SearchExecutionViewModel(this, _searchService);
        Monitor = new ServiceMonitorViewModel(this, _searchService);

        // Clock text that replaces the search box placeholder when empty (opt-in, see #101). No
        // repeating timer -- the quick window is a transient popup, not a resident taskbar clock, so
        // it's enough to recompute this each time the window is actually shown (RefreshLayoutSettings,
        // called from ShowWindow).
        UpdateClockText();

        // Forward property changed notifications from sub-ViewModels
        Search.PropertyChanged += (s, e) =>
        {
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(SelectedResult)) OnPropertyChanged(nameof(PathPreviewVisibility));
            if (e.PropertyName == nameof(SearchExecutionViewModel.IsActionsMode)) OnPropertyChanged(nameof(LaunchPanelVisibility));
            if (e.PropertyName == nameof(ResultsPanelVisibility)) OnPropertyChanged(nameof(LaunchPanelVisibility));
        };
        Monitor.PropertyChanged += (s, e) => OnPropertyChanged(e.PropertyName);

        // The very first startup-panel activation can race a cold service (e.g. RecentFilesTabSource's
        // IPC round trip) into returning early/incomplete data while it's still spinning up, leaving
        // stale tabs with an empty results area -- and IsServiceConnected defaults to true (optimistic,
        // not confirmed), so a normal cold start where the first ping just succeeds never raises a
        // PropertyChanged for it to react to. ServiceBecameReachable fires unconditionally the moment a
        // ping first actually succeeds, so re-running the empty-state fetch here (a no-op if the box
        // isn't empty) settles the panel onto what it should have shown once the service is genuinely
        // there, instead of staying stuck on whatever the cold-start attempt produced.
        Monitor.ServiceBecameReachable += () => Search.RefreshEmptyState();

        // Settings changes push straight into UiMetrics.Scale (see SearchBarLayoutSettingsViewModel.
        // Save()) with no reference back to whichever QuickSearchViewModel/window happens to be open --
        // subscribing here is what lets an already-open window's rows/tabs actually resize the moment
        // the setting is saved, instead of only picking up the new scale the next time this window is
        // shown or a fresh search rebuilds its rows from scratch.
        Services.UiMetrics.ScaleChanged += RefreshScaleBindings;
    }

    public SearchExecutionViewModel Search { get; }
    public ServiceMonitorViewModel Monitor { get; }
    public ObservableRangeCollection<LaunchPanelSourceViewModel> LaunchSources => _launchSources.Sources;
    public LaunchPanelSourceViewModel? SelectedLaunchSource => _launchSources.Selected;

    // ==========================================
    // Delegated Properties for UI Bindings
    // ==========================================

    public ObservableCollection<AppSearchResult> Results => Search.Results;

    public AppSearchResult? SelectedResult
    {
        get => Search.SelectedResult;
        set => Search.SelectedResult = value;
    }

    public Visibility PathPreviewVisibility
    {
        get
        {
            if (IsInlineSearchContext &&
                SelectedResult != null &&
                !SelectedResult.IsEmptyResult &&
                !SelectedResult.IsSearchSectionHeader &&
                !SelectedResult.IsListItem &&
                !SelectedResult.IsPluginSearchAction &&
                !SelectedResult.IsInstantResult &&
                !string.IsNullOrEmpty(SelectedResult.FullPath))
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }
    }

    public string? SearchScope
    {
        get => Search.SearchScope;
        set => Search.SearchScope = value;
    }

    public bool IsInlineSearchContext
    {
        get => Search.IsInlineSearchContext;
        set
        {
            Search.IsInlineSearchContext = value;
            OnPropertyChanged(nameof(LaunchPanelVisibility));
        }
    }

    public string SearchQuery
    {
        get => Search.SearchQuery;
        set
        {
            Search.SearchQuery = value;
            OnPropertyChanged(nameof(LaunchPanelVisibility));
            // The clock takes over the placeholder slot whenever the box is empty (see ClockText) --
            // refresh it right as that happens, not just whenever the window itself was last shown, so
            // clearing a query after the window's been open a while doesn't show stale time.
            if (string.IsNullOrWhiteSpace(value))
                UpdateClockText();
        }
    }

    public bool IsIndexReady => Monitor.IsIndexReady;

    public bool IsServiceConnected => Monitor.IsServiceConnected;

    public string StatusText => Monitor.StatusText;

    public Visibility ErrorIconVisibility => Monitor.ErrorIconVisibility;

    public bool IsSearching => Search.IsSearching;

    public bool IsResultsListEnabled => Search.IsResultsListEnabled;

    public Visibility ResultsPanelVisibility
    {
        get => Search.ResultsPanelVisibility;
        set => Search.ResultsPanelVisibility = value;
    }

    public Visibility ResultsSeparatorVisibility
    {
        get => Search.ResultsSeparatorVisibility;
        set => Search.ResultsSeparatorVisibility = value;
    }

    public Visibility StatusBarVisibility => Monitor.StatusBarVisibility;

    public Visibility LoadingPanelVisibility => Monitor.LoadingPanelVisibility;

    public Visibility ProgressBarVisibility => Monitor.ProgressBarVisibility;

    public Visibility InstallButtonVisibility => Monitor.InstallButtonVisibility;

    public string LoadingTitle => Monitor.LoadingTitle;

    public string LoadingStats => Monitor.LoadingStats;

    public double LoadingProgress
    {
        get => Monitor.LoadingProgress;
        set => Monitor.LoadingProgress = value;
    }

    public bool IsProgressIndeterminate => Monitor.IsProgressIndeterminate;

    public ICommand InstallServiceCommand => Monitor.InstallServiceCommand;

    // ==========================================
    // Delegated Operations
    // ==========================================

    public void TriggerIndexBuild(bool forceRebuild = false) => Monitor.TriggerIndexBuild(forceRebuild);

    // Covers both the Quick and Inline search windows -- they already reuse this one ViewModel, so this
    // is the single call site for both (see InlineSearchWindow.ActivateAndFocusSearchBox,
    // QuickSearchWindowController's own show path, and InlineSearchManager's show path, all of which
    // already call this method on every window activation).
    public void EnsureServiceMonitoringActive()
    {
        Monitor.EnsureServiceMonitoringActive();
        SearchReachabilityGate.BeginSession();
    }
    public void RefreshEmptyState() => Search.RefreshEmptyState();

    public void RefreshLaunchSources() => _ = _launchSources.RefreshAsync();
    public void RefreshLaunchItems() => RefreshLaunchSources();
    public bool CanAcceptLaunchPanelDrops => _launchSources.AcceptsDrops;
    public bool IsManualLaunchSourceSelected => _launchSources.IsManualSourceSelected;
    public int AddLaunchPanelDroppedPaths(IEnumerable<string> paths) => _launchSources.AddDroppedPaths(paths);
    public bool RemoveLaunchPanelItem(AppSearchResult result) => _launchSources.RemoveManualItem(result);

    public ICommand SelectLaunchSourceCommand => new RelayCommand<LaunchPanelSourceViewModel>(source => _launchSources.Select(source));

    public ObservableCollection<AppSearchResult> LaunchPanelItems
        => _launchSources.Selected?.Items ?? EmptyLaunchItems;

    public AppSearchResult? SelectedLaunchPanelItem { get => _launchSources.SelectedItem; set => _launchSources.SelectItem(value); }
    public bool MoveLaunchPanelSelection(int rowDelta, int columnDelta) => _launchSources.MoveItemSelection(rowDelta, columnDelta, LaunchPanelColumns);

    private static readonly ObservableCollection<AppSearchResult> EmptyLaunchItems = new();

    public void CycleLaunchSource(int direction) => _launchSources.Cycle(direction);

    private void OnLaunchSourceChanged(string propertyName) => OnPropertyChanged(propertyName);

    public double SearchBarWidth => UserSettings.Load().SearchWindow.SearchBarWidth;
    public double SearchBarHeight => UserSettings.Load().SearchWindow.SearchBarHeight;
    public int LaunchPanelColumns => Services.UiMetrics.GetLaunchPanelColumns(SearchBarWidth);
    public double LaunchPanelHeight => QuickSearchLaunchPanelHeightCalculator.Calculate(
        LaunchSources, LaunchPanelColumns, LaunchPanelMaxHeight);
    public bool HasMultipleLaunchSources => LaunchSources.Count > 1;

    // Quick window only: InlineSearchWindow shares this same ViewModel class (see
    // InlineSearchManager.EnsureWindowCreated setting IsInlineSearchContext), so without this check the
    // clock would also take over its placeholder even though it's usually mid-task in some other app's
    // window, not an idle "glance at the time" moment the way the Quick window's popup can be.
    public Visibility ClockVisibility => !IsInlineSearchContext && UserSettings.Load().SearchWindow.ShowClock ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LaunchPanelVisibility => !IsInlineSearchContext
        && (!Search.IsActionsMode || ResultsPanelVisibility == Visibility.Visible)
        && string.IsNullOrWhiteSpace(SearchQuery)
        && UserSettings.Load().QuickLaunch.Enabled
        && LaunchSources.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public double LaunchPanelMaxHeight => Services.UiMetrics.ScaledQuickSearchMaxResultHeight;

    // Takes over the search box's own placeholder slot instead of a separate element elsewhere (see
    // SearchBoxControl.xaml's TxtPlaceholder) -- while the box is empty there's nothing to type-hint
    // about, and once typing starts the placeholder (clock included) disappears anyway, so there's no
    // real conflict between "show the time" and "show how to search". Inherits the placeholder's own
    // font size/color, so no separate scaling property is needed here either.
    private string _clockText = string.Empty;
    public string ClockText
    {
        get => _clockText;
        private set => SetProperty(ref _clockText, value);
    }

    private void UpdateClockText()
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo(Services.TranslationManager.Instance.CurrentCulture);
        var now = DateTime.Now;
        var dayName = culture.DateTimeFormat.GetAbbreviatedDayName(now.DayOfWeek);
        // Leading space keeps the text off the caret, which otherwise renders flush against this
        // TextBlock's left edge (same slot as the search box's own cursor).
        ClockText = $" {now.ToString("d", culture)} {dayName} {now:HH:mm}";
    }

    // Called when the window is actually shown (ShowWindow) -- pulls whatever the settings currently
    // say (in case they changed while this window was hidden) and applies them. Live changes while the
    // window is already open/visible go through RefreshScaleBindings via UiMetrics.ScaleChanged instead
    // (see the constructor), since ApplyScaleFromSettings has already run by the time that fires.
    public void RefreshLayoutSettings()
    {
        Services.UiMetrics.ApplyScaleFromSettings();
        RefreshScaleBindings();
    }

    // Re-notifies every binding whose value depends on UiMetrics.Scale, for whatever the CURRENT scale
    // already is -- shared by RefreshLayoutSettings (window just shown) and UiMetrics.ScaleChanged
    // (scale changed live while this window is already open). Deliberately does NOT call
    // ApplyScaleFromSettings itself: that setter is exactly what raises ScaleChanged, so calling it from
    // here too would recurse.
    private void RefreshScaleBindings()
    {
        OnPropertyChanged(nameof(SearchBarWidth));
        OnPropertyChanged(nameof(SearchBarHeight));
        OnPropertyChanged(nameof(LaunchPanelColumns));
        OnPropertyChanged(nameof(ClockVisibility));
        OnPropertyChanged(nameof(LaunchPanelMaxHeight));
        OnPropertyChanged(nameof(LaunchPanelHeight));
        UpdateClockText();

        // Result rows persist as long-lived objects across searches, unlike a freshly-typed search's own
        // AppSearchResult rows, which get built AFTER a scale change and so pick it up on their own --
        // without this, an already-bound row's Scaled* properties stay frozen at whatever they read when
        // it was created, so nothing on screen visibly resizes until the next search rebuilds it.
        foreach (var result in Search.Results)
        {
            result.RefreshScale();
        }
    }

    public void Dispose()
    {
        Services.UiMetrics.ScaleChanged -= RefreshScaleBindings;
        Monitor.StopStatusTimer();
        _searchService.Dispose();
        Search.Dispose();
    }
}
