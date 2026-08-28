using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lertaro.App.Views.SearchWindow;
using Lertaro.App.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;
using ListView = System.Windows.Controls.ListView;
using ListBox = System.Windows.Controls.ListBox;
using Grid = System.Windows.Controls.Grid;
using Lertaro.App.ViewModels.Search;
using Lertaro.App.Services.AppWindow;
using Lertaro.App.Services.ShellIcons;
using Lertaro.App.Services.Tray;
using Lertaro.App.Services.Theme;
using Lertaro.App.Services.ShellMenu.Presenter;
using Lertaro.App.Helpers.Visuals;
namespace Lertaro.App;

public partial class SearchWindow : Window, ISearchWindow, IHasVisibleContentInset
{
    private readonly SearchViewModel _viewModel;
    private readonly SearchWindowChromeHandler _chromeHandler;
    private readonly SearchWindowInputHandler _inputHandler;
    private readonly ShellMenuPresenter _menuPresenter;

    // Must match SearchWindow.xaml's MainBorder Margin.
    public Thickness VisibleContentInset => new(8);

    // Whether to reopen the preview once this window has a first result to show it for. Applied then
    // rather than now because there is nothing to preview yet, and because the quick window this is
    // replacing is still finishing its own hide -- the tail of that clears its query, which re-fires its
    // selection handler and would close a preview opened any earlier.
    private bool _restorePreviewOnFirstResult;
    internal readonly string PowerWindowId = "full:" + Guid.NewGuid().ToString("N");

    public SearchWindow(string initialQuery = "", bool restorePreview = false)
    {
        InitializeComponent();
        _restorePreviewOnFirstResult = restorePreview;

        // Menu only, same as the settings window: custom chrome makes the OS system menu a clipped box
        // with nothing useful in it, but Alt+F4 on a window the user opened and is done with should
        // close it as usual.
        SystemMenuBlocker.Attach(this, blockClose: false);
        ThemedWindowIconHelper.Apply(this);

        // XAML's Height/Width are just the design-time/factory-reset default -- the real size (user's
        // last resize, or the General settings page value) comes from UiMetrics, mirroring how
        // QuickLookManager sizes the preview window from settings instead of a hardcoded XAML value.
        this.Width = UiMetrics.MainWindowWidth;
        this.Height = UiMetrics.MainWindowHeight;

        _menuPresenter = new ShellMenuPresenter(this);
        _chromeHandler = new SearchWindowChromeHandler(this);
        _inputHandler = new SearchWindowInputHandler(this);
        SearchBox.IconLeftClicked += (_, _) => TrayIconService.Instance?.ShowMenuAt(SearchBox, hideShowWindow: true);
        SearchBox.IconMiddleClicked += () => SearchWindowStayOpenSupport.Toggle(this);
        this.PreviewKeyDown += Window_PreviewKeyDown;
        this.StateChanged += SearchWindow_StateChanged;

        // The maximized-size cap that keeps the window off the taskbar is applied per-monitor in
        // SearchWindowChromeHandler.HandleStateChanged, so it stays correct on secondary screens.

        _viewModel = new SearchViewModel(initialQuery);
        this.DataContext = _viewModel;

        QuickLookManager.Instance.Reset();

        this.Loaded += (s, e) =>
        {
            MaximizeBoundsHelper.Attach(this);

            this.Activate();
            this.Focus();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var txtSearch = SearchBox.SearchTextBox;
                txtSearch.Focus();
                Keyboard.Focus(txtSearch);
                if (initialQuery != null)
                {
                    txtSearch.SelectionStart = initialQuery.Length;
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        };

        // Bind list events
        var activeList = ResultsPanelControl.ActiveListBox;
        activeList.KeyDown += LstGridResults_KeyDown;
        activeList.MouseDoubleClick += LstGridResults_MouseDoubleClick;
        activeList.PreviewMouseRightButtonUp += LstGridResults_PreviewMouseRightButtonUp;
        activeList.PreviewMouseWheel += LstGridResults_PreviewMouseWheel;
        activeList.SelectionChanged += (s, e) =>
        {
            if (activeList.SelectedItem is AppSearchResult result && result.CanPreview)
            {
                if (_restorePreviewOnFirstResult)
                {
                    _restorePreviewOnFirstResult = false;
                    QuickLookManager.Instance.Open(this, result.FullPath);
                }
                else
                {
                    QuickLookManager.Instance.UpdateOrShow(this, result.FullPath);
                }
            }
            // No selection at all, with rows present, means the list is mid-rebuild rather than sitting
            // on something unpreviewable. A search that is still streaming repaints several times, and a
            // repaint clears the selection before restoring it -- so treating that gap as "hide" tore the
            // preview down and rebuilt it on every paint, which is what made it flash once and give up.
            else if (activeList.Items.Count == 0 || activeList.SelectedItem != null)
            {
                QuickLookManager.Instance.HideFrom(this);
            }
        };

        ResultsPanelControl.ActionsListBox.PreviewMouseLeftButtonUp += _menuPresenter.HandleActionsPreviewMouseLeftButtonUp;

        TxtSearchBoxControl.ContextMenuOpening += (s, e) => _inputHandler.HandleSearchBoxContextMenuOpening(e);
    }

    // ==========================================

    // ISearchWindow Interface Implementation

    // ==========================================

    public UIElement ResultsPanel => ResultsPanelControl;
    ListBox ISearchWindow.LstResults => ResultsPanelControl.ActiveListBox;
    Grid ISearchWindow.GridSearchResults => ResultsPanelControl.SearchResultsGrid;
    Grid ISearchWindow.GridActions => ResultsPanelControl.ActionsGrid;
    TextBlock ISearchWindow.TxtActionsTarget => ResultsPanelControl.ActionsTargetTextBlock;
    ListBox ISearchWindow.LstActions => ResultsPanelControl.ActionsListBox;
    public string SearchText => SearchBox.SearchTextBox.Text;
    public TextBox SearchTextBox => SearchBox.SearchTextBox;

    public bool IsInActionsMode
    {
        get => SearchBox.IsInActionsMode;
        set
        {
            SearchBox.IsInActionsMode = value;
            _viewModel?.IsActionsMode = value;
        }
    }

    public void UpdateActionsLayout() { /* Fixed-size window, no dynamic resizing needed */ }

    public void FocusSearch()
    {
        SearchBox.SearchTextBox.Focus();
        Keyboard.Focus(SearchBox.SearchTextBox);
    }

    public void OpenFileOrFolderExternal(string path) => FileExecutor.OpenFileOrFolder(path);
    public void OpenFileOrFolderAsAdminExternal(string path) => FileExecutor.OpenFileOrFolderAsAdmin(path);
    public void LocateInExplorerExternal(string path) => FileExecutor.LocateInExplorer(path);
    public void HideWindow() => this.Close();

    // ==========================================

    // Window Control Exposures for Handlers

    // ==========================================

    public TextBox TxtSearchBoxControl => SearchBox.SearchTextBox;
    internal SearchBoxControl SearchBoxControl => SearchBox;
    public ListView LstGridResultsControl => (ListView)ResultsPanelControl.ActiveListBox;
    public ShellMenuPresenter MenuPresenter => _menuPresenter;

    // ==========================================

    // Window Chrome & Drag Handlers

    // ==========================================

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _chromeHandler.HandleHeaderMouseLeftButtonDown(sender, e);
    private void SearchWindow_StateChanged(object? sender, EventArgs e) => _chromeHandler.HandleStateChanged();
    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => _chromeHandler.Minimize();
    private void BtnMaximize_Click(object sender, RoutedEventArgs e) => _chromeHandler.ToggleMaximize();
    private void BtnClose_Click(object sender, RoutedEventArgs e) => _chromeHandler.Close();

    private void BtnBackToQuickSearch_Click(object sender, RoutedEventArgs e)
    {
        var quickSearchWindow = System.Windows.Application.Current.MainWindow as QuickSearchWindow;
        if (quickSearchWindow == null)
        {
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is QuickSearchWindow qsw)
                {
                    quickSearchWindow = qsw;
                    break;
                }
            }
        }

        if (quickSearchWindow != null)
        {
            string? query = null;
            if (!string.IsNullOrWhiteSpace(SearchBox.SearchText))
            {
                query = SearchBox.SearchText;
            }

            else
            {
                query = quickSearchWindow.ViewModel.SearchQuery;
            }

            quickSearchWindow.ShowWindow(query);
        }

        this.Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) => _inputHandler.HandleWindowPreviewKeyDown(e);

    // ==========================================

    // Results Navigation & Context Menu

    // ==========================================

    private void TxtSearchBox_KeyDown(object sender, KeyEventArgs e) => _inputHandler.HandleTxtSearchBoxKeyDown(e);
    private void LstGridResults_MouseDoubleClick(object sender, MouseButtonEventArgs e) => _inputHandler.HandleLstGridResultsMouseDoubleClick(e);
    private void LstGridResults_KeyDown(object sender, KeyEventArgs e) => _inputHandler.HandleLstGridResultsKeyDown(e);
    private void LstGridResults_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) => _inputHandler.HandleLstGridResultsPreviewMouseRightButtonUp(e);

    private void LstGridResults_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            var scrollViewer = FindScrollViewer(LstGridResultsControl);
            if (scrollViewer != null)
            {
                if (e.Delta > 0)
                {
                    scrollViewer.LineLeft();
                    scrollViewer.LineLeft();
                    scrollViewer.LineLeft();
                }

                else
                {
                    scrollViewer.LineRight();
                    scrollViewer.LineRight();
                    scrollViewer.LineRight();
                }

                e.Handled = true;
            }
        }
    }

    private ScrollViewer? FindScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer viewer) return viewer;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
            var result = FindScrollViewer(child);
            if (result != null) return result;
        }

        return null;
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveWindowSize();
        _viewModel.Dispose();
        // This window can be one of several (e.g. opened via "show more"), so on close release the icon
        // cache and trim the working set, matching the quick/inline windows, to reclaim its bitmaps.
        ShellIconHelper.ClearCache();
        PathCacheMaintenance.ClearAllPathCaches();
        Core.Win32Api.TrimWorkingSet();
        PowerThrottlingHelper.WindowHidden(PowerWindowId);
        base.OnClosed(e);
    }

    // Remembers a manual resize-grip drag for next time, the same way QuickSearchWindow remembers its
    // dragged position. Reads RestoreBounds instead of the live Width/Height when closed while maximized,
    // so a maximized session never overwrites the user's actual "normal" size with the maximized one.
    private void SaveWindowSize()
    {
        var size = WindowState == WindowState.Normal ? new Size(Width, Height) : RestoreBounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;

        var settings = Core.UserSettings.Load();
        settings.MainWindow.Width = size.Width;
        settings.MainWindow.Height = size.Height;
        settings.Save();
        UiMetrics.MainWindowWidth = size.Width;
        UiMetrics.MainWindowHeight = size.Height;
    }
}
