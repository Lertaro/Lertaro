using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Controls;
using Lertaro.App.Services;
using Lertaro.Core;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;
using ListBox = System.Windows.Controls.ListBox;
using Grid = System.Windows.Controls.Grid;
using Lertaro.App.ViewModels.Search;
using Lertaro.App.Views.QuickSearchWindow.Helpers;
using LaunchPanel = Lertaro.App.Views.QuickSearchWindow.QuickSearchLaunchPanel;
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
    private readonly QuickSearchLaunchActionsCoordinator _launchActions;
    private bool _isInActionsMode;
    private Action? _scaleChangedHandler;
    internal QuickSearchKeywordHistoryController KeywordHistoryController { get; }

    // Nothing may place this window anywhere until a summon does. The XAML declares it opaque and the
    // constructor parks it off screen, but "off screen" is only worth anything while it stays there: the
    // warm-up show leaves the window shown for an instant before its Hide is processed, and anything that
    // moves it in that window of time -- measured, and it was the DpiChanged handler in this same
    // constructor, which fires because creating the window off every monitor makes WPF report a DPI change
    // -- puts an unpainted opaque window on screen, which is a black rectangle until something repaints
    // that area. Guarding the one caller would have left the next one free, so the guard sits on
    // PositionWindow itself; ShowSupport.ShowWindow clears it once a summon owns the window.
    private bool _suppressPositioning = true;

    // The window is the card: no transparent halo around it, so nothing sits between the window's own edge
    // and its content, and code that docks another window against this one targets the real edge.
    public Thickness VisibleContentInset => new(0);

    public QuickSearchWindow()
    {
        InitializeComponent();
        // Nothing between the XAML's declaration of the handle and the surface this window needs it for.
        ApplySurface();
        // No text-formatting override here any more: this window used to put its search box on Ideal, on
        // its own, to "smooth" the input (d7f6cd49) -- which took the one element the user actually reads
        // off the Display metrics every other element in the process renders with. Section 五 of the plan
        // is what settled that: Display is the app's own default (App.xaml.cs), so the box follows it.
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
        InitializeChildControls();
        KeywordHistoryController = new QuickSearchKeywordHistoryController(this);
        _launchActions = new QuickSearchLaunchActionsCoordinator(this);
        // This window's own monitor changed under it, so the placement has to follow the window, not the mouse.
        this.DpiChanged += (_, _) => { if (IsVisible) PositionWindow(keepCurrentMonitor: true); };
    }

    // The opaque surface this window is. ClearType needs it: a layered window (AllowsTransparency) cannot
    // render sub-pixel antialiasing, so while this window was layered every theme's WindowOpacity -- and
    // every font tweak made to compensate -- was being paid for by the text in it. The XAML declares the
    // opaque background; this only takes the halo out of the layout and parks the window until a summon
    // places it.
    private void ApplySurface()
    {
        CardBorder.Margin = new Thickness(0);
        // Parked off screen right away, because startup shows this window to create its handle and does not
        // hide it until Loaded -- and an opaque window WPF has not painted yet is a black rectangle. Every
        // later show places it first, so nothing has to undo this.
        WindowParking.Park(this);
    }

    // Section 五 asked what this window should set, and the answer is: nothing. TextRenderingMode stays
    // Auto, which is the mode that follows the system's own font-smoothing choice rather than overriding
    // it, and layout rounding and pixel snapping come from AppTypography.OnRootLoaded. TextFormattingMode
    // is no longer forced either -- App.xaml.cs used to override its metadata to Display, and this window
    // had an Ideal override against that, deleted on the reasoning that the app default already covered
    // it. With the app default back at WPF's own Ideal, that deletion is what put the box back where its
    // override wanted it, so the two removals agree rather than cancel.

    public ShellMenuPresenter? MenuPresenter => _menuPresenter;
    public QuickSearchViewModel ViewModel => _viewModel;
    public string SearchText => TxtSearch.Text;
    public bool IsInActionsMode
    {
        get => _isInActionsMode;
        set
        {
            _isInActionsMode = value;
            SearchBox.IsInActionsMode = value && !UsesFloatingActionsMenu;
            if (!value)
                _launchActions.PrepareExit();
            _viewModel.Search.IsActionsMode = value;
            if (!value)
                _launchActions.FinishExit();
        }
    }
    public TextBox TxtSearch => SearchBox.SearchTextBox;
    public TextBox SearchTextBox => SearchBox.SearchTextBox;
    public TextBlock TxtPlaceholder => SearchBox.PlaceholderTextBlock;
    public UIElement ResultsPanel => ResultsPanelControl;
    public ListBox LstResults => ResultsPanelControl.ResultsListBox;
    public Grid GridSearchResults => ResultsPanelControl.SearchResultsGrid;
    internal LaunchPanel LaunchPanel => LaunchPanelControl;
    public Grid GridActions => ResultsPanelControl.ActionsGrid;
    public TextBlock TxtActionsTarget => ResultsPanelControl.ActionsTargetTextBlock;
    public ListBox LstActions => ResultsPanelControl.ActionsListBox;
    public TextBox ActionsSearchTextBox => ResultsPanelControl.ActionsSearchTextBox;
    public bool UsesFloatingActionsMenu => true;
    bool ISearchWindow.KeepWindowOpenAfterActionsHotkey => _launchActions.IsActive;
    internal void EnterLaunchPanelActions(AppSearchResult result) => _launchActions.Enter(result);
    public void UpdateActionsLayout()
    {
        if (GridActions.Visibility != Visibility.Visible)
            _launchActions.PrepareExit();
        _layoutManager.UpdateActionsLayout();
    }
    internal void ExecuteFavorite(AppSearchResult result) => _resultExecutor.Execute(result);
    internal void RecordKeywordHistory()
    {
        try { KeywordHistoryStore.Record(_viewModel.SearchQuery); } catch { }
    }
    // Runs the results-panel height computation synchronously instead of through the normal deferred
    // QueueResultsLayoutUpdate -- see QuickSearchWindowController.ShowWindow's own comment on why it needs
    // this rather than waiting for that callback's usual Normal-priority-deferred pass.
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
        _trayService = new TrayIconService(_viewModel);
        // Wire up event handlers to subcontrols
        SearchBox.IconRightClicked += _controller.ResetPosition;
        SearchBox.IconMiddleClicked += _controller.ToggleStayOpen;
        // The logo is the ONLY way to move this window (#255): the card's own drag was removed rather than
        // fixed, so nothing else on this window responds to a drag. IsIconDraggable keeps the logo's
        // "drag moves the window" behavior working alongside IconLeftClicked: SearchBoxControl tells a real
        // drag apart from a plain click by movement distance (see its own Icon_MouseMove), so this needs
        // BOTH flags rather than picking one.
        SearchBox.IconLeftClicked += (_, _) => ShowTrayMenu();
        SearchBox.IconDragCompleted += SaveWindowPosition;
        SearchBox.IsIconClickable = true;
        SearchBox.IsIconDraggable = true;
        // The gate the logo's drag reads, and it lives in SearchBoxControl rather than here, so "Lock
        // position" has to be pushed to it rather than checked by the drag itself.
        //
        // Refreshed from a tunnelling handler on the window rather than set once above: PreviewMouse...
        // reaches the window before SearchBoxControl's own bubbling icon handler reads the flag, so the
        // logo obeys a toggle the moment it is applied. Anchoring it to a per-show refresh instead would
        // have left it stale for as long as the window stayed open.
        PreviewMouseLeftButtonDown += (_, _) =>
            SearchBox.IsIconDraggable = ShouldAllowIconDrag(UserSettings.Load().SearchWindow.LockPosition);
        SearchBox.IconClickHint = TranslationManager.Instance["QuickSearch_LogoDragResetHint"];
        PreviewMouseDown += (_, e) => _inputHandler.HandleWindowPreviewMouseDown(e);
        LstResults.PreviewMouseLeftButtonUp += (s, e) => _resultExecutor.HandlePreviewMouseLeftButtonUp(e);
        LstResults.PreviewMouseRightButtonUp += (s, e) => _resultExecutor.HandlePreviewMouseRightButtonUp(e);
        LstResults.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnResultsScrollChanged));
        LstActions.PreviewMouseLeftButtonUp += _menuPresenter.HandleActionsPreviewMouseLeftButtonUp;
        // MUST stay deferred, never call ApplyResultsLayout synchronously from here: ReconcileTo can
        // raise many CollectionChanged events in a row (a Replace per changed row, then a RemoveAt per
        // trimmed tail row -- see SearchResultsReconciler/ObservableRangeCollection.ReconcileTo), and
        // LstResults's own ItemContainerGenerator is ALSO a subscriber to this same event, updating its
        // internal bookkeeping in response. Forcing a layout pass mid-batch (ApplyResultsLayout's forced
        // layout does exactly that) before the generator finishes reconciling that same
        // notification throws "ItemsControl inconsistent with its items source" -- confirmed by an actual
        // crash log after trying exactly that. See QuickSearchWindowLayoutManager.QueueResultsLayoutUpdate
        // for why Render priority isn't early enough either (real frame data caught a fully composited,
        // wrong-height frame slipping through first) -- Normal is used instead, still deferred past this
        // synchronous call stack (and so past the generator's own reconciliation) while still outranking
        // the render/paint pass.
        _viewModel.Results.CollectionChanged += (s, e) => _layoutManager.QueueResultsLayoutUpdate();
        // Row/tab heights change in place when the search bar height setting changes live (see
        // QuickSearchViewModel's own UiMetrics.ScaleChanged subscription, which re-notifies each
        // existing row/tab's Scaled* bindings) -- that's a per-item PropertyChanged, not a
        // CollectionChanged, so the subscription above alone wouldn't resize the window to match.
        _scaleChangedHandler = () => _layoutManager.QueueResultsLayoutUpdate();
        UiMetrics.ScaleChanged += _scaleChangedHandler;
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
    public void ShowWindow(string? initialQuery, bool caretAtEnd = false) => _controller.ShowWindow(initialQuery, caretAtEnd);
    // See _suppressPositioning: the warm-up is over once a summon has taken the window over.
    internal void AllowPositioning() => _suppressPositioning = false;
    // Re-armed for the length of the parked frame (see WindowParking.ShowThenPlace): the window is WS_VISIBLE
    // there, so the IsVisible-gated callers are awake again, and any one of them placing it would put an
    // unpainted card where the user is looking -- which is exactly what the parked show exists to avoid.
    internal void SuppressPositioning() => _suppressPositioning = true;
    public void PositionWindow(bool keepCurrentMonitor = false)
    {
        if (_suppressPositioning) return;
        _controller.PositionWindow(keepCurrentMonitor);
    }
    public void HideWindow() => _controller.HideWindow(true);
    public void HideWindowNoRestore() => _controller.HideWindow(false);
    public void SuppressNextForegroundRestore() => _controller.SuppressNextRestore();
    public void ToggleStayOpen() => _controller.ToggleStayOpen();
    public void ToggleVisibility() => _controller.ToggleVisibility();
    public void OpenFileOrFolderExternal(string path)
    {
        RecordKeywordHistory();
        FileExecutor.OpenFileOrFolder(path, TxtSearch.Text, HideWindow);
    }
    public void OpenFileOrFolderAsAdminExternal(string path)
    {
        RecordKeywordHistory();
        FileExecutor.OpenFileOrFolderAsAdmin(path, TxtSearch.Text, HideWindow);
    }
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
    internal static bool ShouldAllowIconDrag(bool lockPosition) => !lockPosition;
    private void SaveWindowPosition() => _controller.SaveWindowPosition();
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) => _inputHandler.HandleWindowPreviewKeyDown(e);
    // The search box logo's own left-click (see SearchBox.IconLeftClicked wiring in the constructor)
    // opens the same menu the tray icon's right-click shows, anchored at the cursor rather than the tray
    // icon's dummy-window+mouse-point placement. "Show Main Window" from here additionally carries over
    // whatever query is currently typed.
    private void ShowTrayMenu()
    {
        var queryText = (IsInActionsMode && _menuPresenter != null) ? _menuPresenter.SavedSearchQuery : TxtSearch.Text;
        _trayService?.ShowMenuAt(RootGrid, () =>
        {
            RecordKeywordHistory();
            FileExecutor.OpenFileOrFolder("__SHOW_MORE__", SearchResultTypePriority.StripLeadingTrigger(queryText), HideWindowNoRestore);
        });
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
        _viewModel.Dispose();
        _trayService?.Dispose();
        _menuPresenter?.Dispose();
        base.OnClosed(e);
    }
}
