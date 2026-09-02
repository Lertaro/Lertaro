using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using Lertaro.App.Services;
using ListBox = System.Windows.Controls.ListBox;
using Lertaro.App.Views.InlineSearchWindow.Helpers;
using Lertaro.App.ViewModels.Search;
using Lertaro.App.Services.AppWindow;

using Lertaro.App.Services.Theme;
using Lertaro.App.Services.ShellMenu.Presenter;
using Lertaro.App.Helpers.Visuals;
namespace Lertaro.App;

/// <summary>
/// Compact inline search window that appears at the bottom-right corner of
/// the active Explorer window or Desktop when the user types any character.
/// Results expand upward, search box stays anchored at bottom.
/// </summary>
public partial class InlineSearchWindow : Window, ISearchWindow
{
    private readonly QuickSearchViewModel _viewModel;
    private readonly InlineSearchManager _manager;
    private readonly ShellMenuPresenter _menuPresenter;
    private readonly InlineSearchWindowInputHandler _inputHandler;
    private bool _isImeComposing;
    private readonly InlineSearchWindowPositioner _positioner;
    private readonly DispatcherTimer _activeTimer;
    private string _searchText = string.Empty;
    private IntPtr _originalLayout = IntPtr.Zero;
    public ShellMenuPresenter MenuPresenter => _menuPresenter;
    public QuickSearchViewModel ViewModel => _viewModel;
    public InlineSearchManager Manager => _manager;
    public InlineSearchWindowInputHandler InputHandler => _inputHandler;
    public InlineSearchWindowPositioner Positioner => _positioner;

    // Window-wide (not just the results ListBox -- see ResultsDragDropHelper's own down/up tracking,
    // which is scoped to just that control) record of "a left-button press landed somewhere in this
    // window and hasn't been matched by a release yet". WPF's mouse button state can end up reporting
    // Pressed during a later plain hover with NO down/up ever observed on the results list at all --
    // meaning the press landed on some OTHER part of this window (a margin, the search box, etc.),
    // and this window was then torn down (CloseInlineSearch) before the
    // matching release ever reached any Lertaro-owned window to clear it, leaving it stuck on the
    // next window's hover. CloseInlineSearch checks this before destroying the window.
    public bool HasPendingMouseDown { get; private set; }

    public InlineSearchWindow(QuickSearchViewModel viewModel, InlineSearchManager manager)
    {
        InitializeComponent();
        ThemedWindowIconHelper.Apply(this);
        SystemMenuBlocker.Attach(this);
        _viewModel = viewModel;
        _manager = manager;
        this.DataContext = _viewModel;
        _menuPresenter = new ShellMenuPresenter(this);
        _inputHandler = new InlineSearchWindowInputHandler(this);
        TextCompositionManager.AddPreviewTextInputStartHandler(SearchBox.SearchTextBox, (_, _) => _isImeComposing = true);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(SearchBox.SearchTextBox, (_, _) => _isImeComposing = true);
        TextCompositionManager.AddPreviewTextInputHandler(SearchBox.SearchTextBox, (_, _) => _isImeComposing = false);
        SearchBox.SearchTextBox.PreviewKeyDown += (_, e) => _isImeComposing = e.Key == Key.Escape ? false : _isImeComposing;
        SearchBox.SearchTextBox.LostKeyboardFocus += (_, _) => _isImeComposing = false;
        _positioner = new InlineSearchWindowPositioner(this);
        PreviewMouseLeftButtonDown += (_, _) => HasPendingMouseDown = true;
        PreviewMouseLeftButtonUp += (_, _) => HasPendingMouseDown = false;
        _activeTimer = new DispatcherTimer(DispatcherPriority.Background);
        _activeTimer.Interval = TimeSpan.FromMilliseconds(100);

        _activeTimer.Tick += (s, e) =>
        {
            var tracker = _manager.ExplorerTracker;
            if (tracker.IsActiveWindowDialog && tracker.ActiveHwnd != IntPtr.Zero)
            {
                if (!InlineSearchWindowNativeMethods.IsWindow(tracker.ActiveHwnd))
                {
                    _activeTimer.Stop();
                    _manager.CloseInlineSearch();
                }
            }

        };
        _activeTimer.Start();

        SearchBox.SearchTextBox.TextChanged += (s, e) =>
        {
            if (_isImeComposing) return;
            // While in Actions mode, ShellMenuPresenter owns this text (filtering the actions list, and
            // restoring the saved query on exit) -- treating either as "new typing" here would wipe the
            // results selection/scroll position the user had before entering the actions menu.
            if (_menuPresenter.IsInActionsMode) return;

            if (_searchText != SearchBox.SearchTextBox.Text)
            {
                _viewModel.IsInlineSearchContext = true;
                _searchText = SearchBox.SearchTextBox.Text;
                LstResults.SelectedIndex = -1;
                _inputHandler.ResetUserNavigation();
                _viewModel.SearchQuery = _searchText;
            }

        };
        this.PreviewKeyDown += (s, e) => _inputHandler.HandlePreviewKeyDown(e);

        // Use custom template for inline search that hides path/ParentDir

        if (TryFindResource("InlineSearchResultTemplate") is DataTemplate inlineTemplate)
        {
            LstResults.ItemTemplate = inlineTemplate;
        }

        _manager.ExplorerTracker.OnActiveWindowMoved += HandleActiveWindowMoved;

        InlineSearchWindowQuickNavWiring.Attach(this);

        this.IsVisibleChanged += (s, e) =>
        {
            if (IsVisible)
            {
                _positioner.PositionWindow();

                // Re-arm the results height fixup on becoming visible. QueueResultsLayoutUpdate's deferred
                // callback bails out if the window isn't visible at the moment it finally runs (e.g. raced by
                // a rapid show/hide), and nothing else ever retries it -- so without this, LstResults can be
                // stuck at its XAML-default Height="0" (invisible) until some unrelated event happens to
                // repopulate Results. Re-queuing here closes that gap regardless of what caused the miss.
                _inputHandler.QueueResultsLayoutUpdate();
            }

        };

        this.SourceInitialized += (s, e) =>
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var hwnd = helper.Handle;
            if (hwnd != IntPtr.Zero)
            {
                // 1. Decouple window hierarchy: Set active Explorer/Desktop as native owner HWND

                var tracker = _manager.ExplorerTracker;
                if (tracker.ActiveHwnd != IntPtr.Zero)
                {
                    InlineSearchWindowNativeMethods.SetWindowLongPtr(hwnd, InlineSearchWindowNativeMethods.GWL_HWNDPARENT, tracker.ActiveHwnd);
                }

                // 2. Set Extended Styles: WS_EX_TOOLWINDOW (hide from Alt+Tab)

                var exStyle = InlineSearchWindowNativeMethods.GetWindowLongPtr(hwnd, InlineSearchWindowNativeMethods.GWL_EXSTYLE);
                exStyle = new IntPtr(exStyle.ToInt64() | InlineSearchWindowNativeMethods.WS_EX_TOOLWINDOW);
                InlineSearchWindowNativeMethods.SetWindowLongPtr(hwnd, InlineSearchWindowNativeMethods.GWL_EXSTYLE, exStyle);

                // 3. Ensure topmost

                InlineSearchWindowNativeMethods.SetWindowPos(hwnd, InlineSearchWindowNativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    InlineSearchWindowNativeMethods.SWP_NOMOVE | InlineSearchWindowNativeMethods.SWP_NOSIZE | InlineSearchWindowNativeMethods.SWP_SHOWWINDOW);
            }

            if (IsVisible) _positioner.PositionWindow();
        };

        this.Loaded += (s, e) =>
        {
            if (IsVisible) _positioner.PositionWindow();
        };

        this.SizeChanged += (s, e) =>
        {
            if (IsVisible)
            {
                _positioner.PositionWindow();
            }

        };

        _viewModel.Results.CollectionChanged += (s, e) =>
        {
            _inputHandler.SuppressExplorerSelectionSyncForResultRefresh();
            _inputHandler.QueueResultsLayoutUpdate();
        };

        // Wire up results/actions list scroll, selection, and mouse-click handlers.
        InlineSearchWindowResultsWiring.Attach(this);
    }

    // ==========================================
    // Exposed Child Controls matching QuickSearchWindow for ShellMenuPresenter

    // ==========================================

    public UIElement ResultsPanel => ResultsPanelControl;
    public ListBox LstResults => ResultsPanelControl.ResultsListBox;
    public Grid GridSearchResults => ResultsPanelControl.SearchResultsGrid;
    public Grid GridActions => ResultsPanelControl.ActionsGrid;
    public TextBlock TxtActionsTarget => ResultsPanelControl.ActionsTargetTextBlock;
    public ListBox LstActions => ResultsPanelControl.LstActions;
    public bool UsesFloatingActionsMenu => false;
    public string SearchText => _searchText;
    public System.Windows.Controls.TextBox SearchTextBox => SearchBox.SearchTextBox;

    public bool IsInActionsMode
    {
        get => SearchBox.IsInActionsMode;
        set
        {
            SearchBox.IsInActionsMode = value;
            _viewModel.Search.IsActionsMode = value;
        }
    }

    public bool ActivateAndFocusSearchBox()
    {
        _viewModel.EnsureServiceMonitoringActive();
        var foreground = InlineSearchWindowNativeMethods.GetForegroundWindow();
        var currentThread = InlineSearchWindowNativeMethods.GetCurrentThreadId();

        var foregroundThread = foreground != IntPtr.Zero

            ? InlineSearchWindowNativeMethods.GetWindowThreadProcessId(foreground, out _)

            : 0;
        var attached = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
                attached = InlineSearchWindowNativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
            Activate();
            SearchBox.SearchTextBox.Focus();
            Keyboard.Focus(SearchBox.SearchTextBox);
            SearchBox.SearchTextBox.CaretIndex = SearchBox.SearchTextBox.Text.Length;
            if (foreground != IntPtr.Zero && foregroundThread != 0)
            {
                _originalLayout = InlineSearchWindowNativeMethods.GetKeyboardLayout(currentThread);
                var layout = InlineSearchWindowNativeMethods.GetKeyboardLayout(foregroundThread);
                if (layout != IntPtr.Zero)
                {
                    InlineSearchWindowNativeMethods.ActivateKeyboardLayout(layout, 0);
                }
            }

            return IsActive && SearchBox.SearchTextBox.IsKeyboardFocusWithin;
        }

        finally
        {
            if (attached)
                InlineSearchWindowNativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    public void HideWindow() => _manager.CloseInlineSearch();

    public void UpdateSearchDisplay(string text)
    {
        _searchText = text;
        LstResults.SelectedIndex = -1;
        _inputHandler.ResetUserNavigation();
        SearchBox.SearchTextBox.Text = text;
        SearchBox.SearchTextBox.CaretIndex = SearchBox.SearchTextBox.Text.Length;
        _viewModel.SearchQuery = text;
    }

    public void UpdateActionsLayout() => _inputHandler.UpdateActionsLayout();
    public void FocusSearch()
    {
        SearchBox.SearchTextBox.Focus();
        Keyboard.Focus(SearchBox.SearchTextBox);
    }
    public void LaunchByShortcutIndex(int num) => _inputHandler.LaunchByShortcutIndex(num);
    public void OpenFileOrFolderExternal(string path) => InlineSearchNavigator.OpenFileOrFolderExternal(this, path);
    public void OpenFileOrFolderAsAdminExternal(string path) => InlineSearchNavigator.OpenFileOrFolderAsAdminExternal(this, path);
    public void LocateInExplorerExternal(string path) => InlineSearchNavigator.LocateInExplorerExternal(this, path);
    public void ExecuteSearchResult(AppSearchResult result) => InlineSearchNavigator.ExecuteSearchResult(this, result);
    public void ExecuteSearchResultAsAdmin(AppSearchResult result) => InlineSearchNavigator.ExecuteSearchResult(this, result, asAdmin: true);
    public bool IsPointInsideWindowExternal(int x, int y) => InlineSearchWindowNativeMethods.IsPointInsideWindow(x, y);
    private void HandleActiveWindowMoved()
    {
        if (IsVisible)
        {
            _positioner.PositionWindow();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _activeTimer?.Stop();
        _manager.ExplorerTracker.OnActiveWindowMoved -= HandleActiveWindowMoved;
        _menuPresenter.Dispose();
        // Cancel the in-flight search and release the per-session search service (this window's
        // view model is its own instance), matching the quick/full search windows.
        _viewModel.Dispose();
        if (_originalLayout != IntPtr.Zero)
        {
            InlineSearchWindowNativeMethods.ActivateKeyboardLayout(_originalLayout, 0);
            _originalLayout = IntPtr.Zero;
        }

        base.OnClosed(e);
    }
}
