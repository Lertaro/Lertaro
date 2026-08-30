using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Controls;
using Lertaro.App.Services;
using Lertaro.Core;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;
using Border = System.Windows.Controls.Border;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using Grid = System.Windows.Controls.Grid;
using Lertaro.App.ViewModels.Search;
using Lertaro.App.Views.QuickSearchWindow.Helpers;
using Lertaro.App.Services.AppWindow;
using Lertaro.App.Services.Tray;
using Lertaro.App.Services.Theme;
using Lertaro.App.Services.ShellMenu.Presenter;
using Lertaro.App.Helpers.Visuals;
namespace Lertaro.App;
public partial class QuickSearchWindow : Window, ISearchWindow, IHasVisibleContentInset
{
    private readonly QuickSearchViewModel _viewModel;
    private TrayIconService? _trayService;
    private ShellMenuPresenter? _menuPresenter;
    private readonly QuickSearchWindowController _controller;
    private readonly QuickSearchWindowInputHandler _inputHandler;
    private readonly QuickSearchWindowLayoutManager _layoutManager;
    private readonly QuickSearchWindowResultExecutor _resultExecutor;
    private readonly QuickSearchWindowLifecycle _lifecycle;
    private readonly QuickSearchWindowDragSupport _dragSupport;
    private readonly QuickSearchDomainUrlSupport _domainUrlSupport;
    private Action? _scaleChangedHandler;
    internal QuickSearchKeywordHistoryController KeywordHistoryController { get; private set; } = null!;
    // Must match QuickSearchWindow.xaml's root Border Margin ("24,40,24,24").
    public Thickness VisibleContentInset => new(24, 40, 24, 24);
    public QuickSearchWindow()
    {
        InitializeComponent();
        ThemedWindowIconHelper.Apply(this);
        SystemMenuBlocker.Attach(this);
        _viewModel = new QuickSearchViewModel();
        this.DataContext = _viewModel;
        _controller = new QuickSearchWindowController(this);
        // Mirrors the flag onto the logo. Subscribed rather than set from ToggleStayOpen alone, because
        // the flag also clears itself on the next real hide (see the controller's FinishHide).
        _controller.StayOpenChanged += stayOpen => SearchBox?.IsStayOpen = stayOpen;
        _inputHandler = new QuickSearchWindowInputHandler(this);
        _layoutManager = new QuickSearchWindowLayoutManager(this);
        _resultExecutor = new QuickSearchWindowResultExecutor(this);
        _lifecycle = new QuickSearchWindowLifecycle(this, () => _trayService?.HandleTaskbarCreated());
        _dragSupport = new QuickSearchWindowDragSupport(this);
        InitializeChildControls();
        _domainUrlSupport = new QuickSearchDomainUrlSupport(this);
    }
    public ShellMenuPresenter? MenuPresenter => _menuPresenter;
    public QuickSearchViewModel ViewModel => _viewModel;
    public string SearchText => TxtSearch.Text;
    public bool IsInActionsMode
    {
        get => SearchBox.IsInActionsMode;
        set
        {
            SearchBox.IsInActionsMode = value;
            _viewModel.Search.IsActionsMode = value;
        }
    }
    public TextBox TxtSearch => SearchBox.SearchTextBox;
    public TextBox SearchTextBox => SearchBox.SearchTextBox;
    public TextBlock TxtPlaceholder => SearchBox.PlaceholderTextBlock;
    public UIElement ResultsPanel => ResultsPanelControl;
    public Border GridLoading => ResultsPanelControl.LoadingBorder;
    public System.Windows.Controls.Control ProgressLoading => ResultsPanelControl.LoadingProgressBar;
    public TextBlock TxtLoadingTitle => ResultsPanelControl.LoadingTitleTextBlock;
    public TextBlock TxtLoadingStats => ResultsPanelControl.LoadingStatsTextBlock;
    public Button BtnInstallService => ResultsPanelControl.InstallServiceButton;
    public ListBox LstResults => ResultsPanelControl.ResultsListBox;
    public Grid GridSearchResults => ResultsPanelControl.SearchResultsGrid;
    public Grid GridActions => ResultsPanelControl.ActionsGrid;
    public TextBlock TxtActionsTarget => ResultsPanelControl.ActionsTargetTextBlock;
    public ListBox LstActions => ResultsPanelControl.ActionsListBox;
    public void UpdateActionsLayout() => _layoutManager.UpdateActionsLayout();
    internal void ExecuteFavorite(AppSearchResult result) => _resultExecutor.Execute(result);
    // Runs the results-panel height computation synchronously instead of through the normal deferred
    // QueueResultsLayoutUpdate -- see QuickSearchWindowController.ShowWindow's own comment on why it needs
    // this rather than waiting for that callback's usual Send-priority-deferred pass.
    public void ApplyResultsLayoutImmediate() => _layoutManager.ApplyResultsLayout();
    public void FocusSearch()
    {
        TxtSearch.Focus();
        Keyboard.Focus(TxtSearch);
    }
    public UIElement StatusBar => StatusBarControl;
    public System.Windows.Shapes.Ellipse DotStatus => StatusBarControl.StatusDot;
    public TextBlock TxtStatusInfo => StatusBarControl.StatusInfoTextBlock;
    private void InitializeChildControls()
    {
        _menuPresenter = new ShellMenuPresenter(this);
        _trayService = new TrayIconService(_viewModel, ShowWindow, ToggleVisibility);
        // Wire up event handlers to subcontrols
        SearchBox.IconRightClicked += _controller.ResetPosition;
        SearchBox.IconMiddleClicked += _controller.ToggleStayOpen;
        // IsIconDraggable keeps the logo's existing "drag moves the window" behavior working alongside
        // IconLeftClicked: SearchBoxControl tells a real drag apart from a plain click by movement
        // distance (see its own Icon_MouseMove), so this needs BOTH flags rather than picking one.
        SearchBox.IconLeftClicked += (_, _) => ShowTrayMenu();
        SearchBox.IconDragCompleted += SaveWindowPosition;
        SearchBox.IsIconClickable = true;
        SearchBox.IsIconDraggable = true;
        // The logo is a SECOND way to drag this window, and it lives in SearchBoxControl rather than
        // here, so gating Border_MouseLeftButtonDown alone left it moving while "Lock position" was on.
        //
        // Refreshed from a tunnelling handler on the window rather than set once above: PreviewMouse...
        // reaches the window before SearchBoxControl's own bubbling icon handler reads the flag, so the
        // logo obeys a toggle the moment it is applied -- the same per-press freshness the border drag
        // gets from reading the setting inline. Anchoring it to a per-show refresh instead would have
        // left the two paths disagreeing for as long as the window stayed open.
        PreviewMouseLeftButtonDown += (_, _) =>
            SearchBox.IsIconDraggable = ShouldAllowIconDrag(UserSettings.Load().SearchWindow.LockPosition);
        SearchBox.IconClickHint = TranslationManager.Instance["QuickSearch_LogoDragResetHint"];
        LstResults.PreviewMouseLeftButtonUp += (s, e) => _resultExecutor.HandlePreviewMouseLeftButtonUp(e);
        LstResults.PreviewMouseRightButtonUp += (s, e) => _resultExecutor.HandlePreviewMouseRightButtonUp(e);
        LstResults.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnResultsScrollChanged));
        LstActions.PreviewMouseLeftButtonUp += _menuPresenter.HandleActionsPreviewMouseLeftButtonUp;
        // MUST stay deferred, never call ApplyResultsLayout synchronously from here: ReconcileTo can
        // raise many CollectionChanged events in a row (a Replace per changed row, then a RemoveAt per
        // trimmed tail row -- see SearchResultsReconciler/ObservableRangeCollection.ReconcileTo), and
        // LstResults's own ItemContainerGenerator is ALSO a subscriber to this same event, updating its
        // internal bookkeeping in response. Forcing a layout pass mid-batch (ApplyResultsLayout's
        // SizeToContent toggle does exactly that) before the generator finishes reconciling that same
        // notification throws "ItemsControl inconsistent with its items source" -- confirmed by an actual
        // crash log after trying exactly that. See QuickSearchWindowLayoutManager.QueueResultsLayoutUpdate
        // for why Render priority isn't early enough either (real frame data caught a fully composited,
        // wrong-height frame slipping through first) -- Send is used instead, still deferred past this
        // synchronous call stack (and so past the generator's own reconciliation) but higher priority than
        // the render/paint pass.
        _viewModel.Results.CollectionChanged += (s, e) => _layoutManager.QueueResultsLayoutUpdate();
        // Row/tab heights change in place when the search bar height setting changes live (see
        // QuickSearchViewModel's own UiMetrics.ScaleChanged subscription, which re-notifies each
        // existing row/tab's Scaled* bindings) -- that's a per-item PropertyChanged, not a
        // CollectionChanged, so the subscription above alone wouldn't resize the window to match.
        _scaleChangedHandler = () => _layoutManager.QueueResultsLayoutUpdate();
        UiMetrics.ScaleChanged += _scaleChangedHandler;
        KeywordHistoryController = new QuickSearchKeywordHistoryController(this);
        LstResults.SelectionChanged += (s, e) =>
        {
            if (LstResults.SelectedItem is AppSearchResult result && result.CanPreview)
            {
                QuickLookManager.Instance.UpdateOrShow(this, result.FullPath);
            }
            else if (LstResults.Items.Count == 0 || LstResults.SelectedItem != null)
            {
                QuickLookManager.Instance.HideFrom(this);
            }
        };
    }
    private void OnResultsScrollChanged(object sender, ScrollChangedEventArgs e) => _layoutManager.UpdateShortcutHints();
    public void UpdateShortcutHints() => _layoutManager.UpdateShortcutHints();
    // Fires as soon as the Win32 HWND exists, and again once the window has fully loaded -- see
    // QuickSearchWindowLifecycle for why each step runs when it does (hardware-acceleration opt-out,
    // Alt+Tab hiding, tray-icon re-add, theme effects, first-launch focus/hide).
    private void Window_SourceInitialized(object? sender, EventArgs e) => _lifecycle.HandleSourceInitialized();
    private void Window_Loaded(object sender, RoutedEventArgs e) => _lifecycle.HandleLoaded();
    // Called by GeneralSettingsViewModel.Apply() when the "hide tray icon" setting changes, so the
    // real NotifyIcon updates live without needing a restart. The search box logo's own menu (see
    // ShowTrayMenu) is unaffected by this setting -- it's a permanent, always-visible entry point
    // regardless of the tray icon's state.
    public void ApplyTrayIconVisibility(bool hideTrayIcon) => _trayService?.SetTrayIconVisible(!hideTrayIcon);
    public void ShowWindow() => _controller.ShowWindow(null);
    public void ShowWindow(string? initialQuery) => _controller.ShowWindow(initialQuery);
    public void PositionWindow() => _controller.PositionWindow();
    public void HideWindow() => _controller.HideWindow(true);
    public void HideWindowNoRestore() => _controller.HideWindow(false);
    public void SuppressNextForegroundRestore() => _controller.SuppressNextRestore();
    public void ToggleStayOpen() => _controller.ToggleStayOpen();
    public void ToggleVisibility() => _controller.ToggleVisibility();
    public void OpenFileOrFolderExternal(string path) => FileExecutor.OpenFileOrFolder(path, TxtSearch.Text, HideWindow);
    public void OpenFileOrFolderAsAdminExternal(string path) => FileExecutor.OpenFileOrFolderAsAdmin(path, TxtSearch.Text, HideWindow);
    public void LocateInExplorerExternal(string path) => FileExecutor.LocateInExplorer(path);
    public static T? FindVisualParentExternal<T>(DependencyObject? child) where T : DependencyObject => FindVisualParent<T>(child);
    private void Window_Deactivated(object sender, EventArgs e)
    {
        // A transient foreground steal can deactivate us mid-typing -- e.g. reading a \\wsl$ result's icon
        // or modified date on a background thread wakes the WSL VM, whose cold start briefly flashes a
        // conhost that grabs the foreground. Wait a moment and only hide if still inactive, so focus that
        // bounces right back doesn't drop the search window.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (s, _) =>
        {
            timer.Stop();
            if (IsActive) return;
            // Already hidden, so there is nothing here to react to -- something else put this window away
            // and losing activation is just a consequence of that. Without this, opening the full window
            // ran a second, full HideWindow() over the top of the deliberate one: two hundred milliseconds
            // after the full window had opened the preview it inherited, this tore it down again through
            // QuickLookManager.Reset, which is global and by then belonged to the other window. Traced from
            // the symptom -- the preview appearing for an instant and never returning.
            if (!IsVisible) return;
            // QuickLookManager just Hide()'d this window itself, for a preview handler's own popup
            // dialog (see its own comment) -- not a real deactivation to react to.
            if (QuickLookManager.Instance.IsHiddenForDialog) return;
            // An out-of-process preview (native handler, or an external app like QuickLook docked via
            // QuickLookBridge) legitimately holds OS foreground right now -- see PreviewActivationSignal's
            // own doc comment. QuickSearchWindowForegroundWatcher already checks this same signal for its
            // own (WinEventHook-driven) hide path; this is the plain Window.Deactivated path, which fires
            // independently and needs the same guard.
            if (PluginSdk.Services.PreviewActivationSignal.IsActive) return;
            // Do not hide if there are visible owned windows (e.g. a crash MessageBox dialog).
            foreach (Window owned in OwnedWindows)
                if (owned.IsVisible) return;
            // If activation moved to one of our own other windows (e.g. Settings/About opened via the
            // search box logo's own menu), restoring foreground to whatever was active before this
            // window was shown would immediately steal it right back off that window. Only restore focus
            // when the user has actually left the app entirely.
            var movedToOwnWindow = false;
            foreach (Window w in System.Windows.Application.Current.Windows)
                if (w != this && w.IsActive) { movedToOwnWindow = true; break; }
            _controller.HideOnFocusLoss(restoreFocus: !movedToOwnWindow);
        };
        timer.Start();
    }
    internal static bool ShouldStartDrag(MouseButton changedButton, bool lockPosition)
        => QuickSearchWindowDragSupport.ShouldStartDrag(changedButton, lockPosition);
    internal static bool ShouldAllowIconDrag(bool lockPosition) => QuickSearchWindowDragSupport.ShouldAllowIconDrag(lockPosition);
    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _dragSupport.OnMouseLeftButtonDown(sender, e);
    private void Border_MouseMove(object sender, MouseEventArgs e) => _dragSupport.OnMouseMove(sender, e);
    private void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _dragSupport.OnMouseLeftButtonUp(sender, e);
    private void SaveWindowPosition() => _controller.SaveWindowPosition();
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) => _inputHandler.HandleWindowPreviewKeyDown(e);
    // The search box logo's own left-click (see SearchBox.IconLeftClicked wiring in the constructor)
    // opens the same menu the tray icon's right-click shows, anchored at the cursor rather than the tray
    // icon's dummy-window+mouse-point placement. "Show Main Window" from here additionally carries over
    // whatever query is currently typed.
    private void ShowTrayMenu()
    {
        var queryText = (IsInActionsMode && _menuPresenter != null) ? _menuPresenter.SavedSearchQuery : TxtSearch.Text;
        _trayService?.ShowMenuAt(RootGrid, () => FileExecutor.OpenFileOrFolder("__SHOW_MORE__", SearchResultTypePriority.StripLeadingTrigger(queryText), HideWindowNoRestore));
    }
    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            if (child is FrameworkContentElement fce)
            {
                child = fce.Parent;
            }
            else
            {
                child = System.Windows.Media.VisualTreeHelper.GetParent(child);
            }
        }
        return null;
    }
    protected override void OnClosed(EventArgs e)
    {
        if (_scaleChangedHandler != null)
            UiMetrics.ScaleChanged -= _scaleChangedHandler;
        _domainUrlSupport.Dispose();
        _viewModel.Dispose();
        _trayService?.Dispose();
        _menuPresenter?.Dispose();
        base.OnClosed(e);
    }
}
