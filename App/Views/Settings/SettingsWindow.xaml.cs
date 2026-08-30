using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lertaro.App.ViewModels.Settings;

using Lertaro.App.Services.ShellIcons;
using Lertaro.App.Services.Theme;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Views.Settings;
using Lertaro.App.Views.Settings.General;
using Lertaro.App.Views.Settings.Hotkey;
using Lertaro.App.Views.Settings.Plugins;
namespace Lertaro.App;

// Window chrome and the sidebar's own section-switching. Search box/popup logic lives in
// SettingsWindowSearchExtensions.cs (extension methods, kept separate to stay under the file-length
// convention) -- the three XAML-wired event handlers below stay here since XAML event wiring resolves
// by reflection and can't target an extension method.
public partial class SettingsWindow : Window
{
    private int _validationErrorCount;

    public static readonly DependencyProperty IsSidebarCompactProperty = DependencyProperty.Register(
        nameof(IsSidebarCompact), typeof(bool), typeof(SettingsWindow));

    public bool IsSidebarCompact
    {
        get => (bool)GetValue(IsSidebarCompactProperty);
        private set => SetValue(IsSidebarCompactProperty, value);
    }

    // Lazily constructed on first visit instead of all being built (and their full visual trees
    // realized) up front -- see issue #186: opening even a single cheap tab like About used to pay for
    // every other tab's construction too. AddPage parents each one into PagesHost (see SettingsWindow.xaml)
    // the first time its property is touched; ApplySelectedSection only ever touches the tab it's
    // switching to (plus whichever was already visible), never the untouched ones, so tabs the user never
    // visits stay unbuilt for the whole window's lifetime.
    private ServiceSettingsPage? _pageService;
    private IndexSettingsPage? _pageIndex;
    private GeneralSettingsPage? _pageGeneral;
    private AppearanceSettingsPage? _pageAppearance;
    private HotkeySettingsPage? _pageHotkeys;
    private PluginManagementSettingsPage? _pagePlugins;
    private HistorySettingsPage? _pageHistory;
    private FavoritesSettingsPage? _pageFavorites;
    private Views.Settings.QuickLaunch.QuickLaunchSettingsPage? _pageQuickLaunch;
    private Views.Settings.QuickPanel.QuickPanelSettingsPage? _pageQuickPanel;
    private Views.Settings.LocalSend.LocalSendSettingsPage? _pageLocalSend;
    private AboutSettingsPage? _pageAbout;
    private FrameworkElement? _currentPage;

    internal ServiceSettingsPage PageService => _pageService ??= AddPage(new ServiceSettingsPage());
    internal IndexSettingsPage PageIndex => _pageIndex ??= AddPage(new IndexSettingsPage());
    internal GeneralSettingsPage PageGeneral => _pageGeneral ??= AddPage(new GeneralSettingsPage());
    internal AppearanceSettingsPage PageAppearance => _pageAppearance ??= AddPage(new AppearanceSettingsPage());
    // Hotkeys/Plugins/History/Favorites set their own DataContext explicitly (previously done
    // via SettingsWindow.xaml's DataContext="{Binding Xxx}") -- the rest inherit it from this Window like
    // before, since inherited DataContext still flows correctly through a subtree added via
    // Children.Add rather than markup.
    internal HotkeySettingsPage PageHotkeys => _pageHotkeys ??= AddPage(new HotkeySettingsPage { DataContext = ((SettingsViewModel)DataContext).Hotkeys });
    internal PluginManagementSettingsPage PagePlugins => _pagePlugins ??= AddPage(new PluginManagementSettingsPage { DataContext = ((SettingsViewModel)DataContext).Plugins });
    internal HistorySettingsPage PageHistory => _pageHistory ??= AddPage(new HistorySettingsPage { DataContext = ((SettingsViewModel)DataContext).History });
    internal FavoritesSettingsPage PageFavorites => _pageFavorites ??= AddPage(new FavoritesSettingsPage { DataContext = ((SettingsViewModel)DataContext).Favorites });
    internal Views.Settings.QuickLaunch.QuickLaunchSettingsPage PageQuickLaunch => _pageQuickLaunch ??= AddPage(new Views.Settings.QuickLaunch.QuickLaunchSettingsPage { DataContext = ((SettingsViewModel)DataContext).QuickLaunch });
    internal Views.Settings.QuickPanel.QuickPanelSettingsPage PageQuickPanel => _pageQuickPanel ??= AddPage(new Views.Settings.QuickPanel.QuickPanelSettingsPage { DataContext = ((SettingsViewModel)DataContext).QuickPanel });
    internal Views.Settings.LocalSend.LocalSendSettingsPage PageLocalSend => _pageLocalSend ??= AddPage(new Views.Settings.LocalSend.LocalSendSettingsPage { DataContext = ((SettingsViewModel)DataContext).LocalSend });
    internal AboutSettingsPage PageAbout => _pageAbout ??= AddPage(new AboutSettingsPage());

    private T AddPage<T>(T page) where T : FrameworkElement
    {
        page.Visibility = Visibility.Collapsed;
        PagesHost.Children.Add(page);
        return page;
    }

    public SettingsWindow()
    {
        InitializeComponent();
        // Menu only. This window has custom chrome, so Alt+Space would drop an OS-drawn box clipped by
        // its own rounded corners, but it is an ordinary window the user opens and is done with, so
        // Alt+F4 stays working.
        SystemMenuBlocker.Attach(this, blockClose: false);
        // Same WM_GETMINMAXINFO interception the full search window uses. A borderless window maximizes
        // to the whole monitor rather than its work area, so without this it covers the taskbar.
        MaximizeBoundsHelper.Attach(this);
        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo, this);
        var vm = new SettingsViewModel();
        DataContext = vm;
        Loaded += (_, _) =>
        {
            UpdateSidebarLayout(ActualWidth);
            if (LstSections.SelectedItem == null && LstSectionsBottom.SelectedItem == null) LstSections.SelectedIndex = 0;
            FocusSearchBox();
        };
        Closed += (_, _) =>
        {
            vm.Cleanup();
            // Release cached bitmaps and trim the working set on close, like the search windows.
            ShellIconHelper.ClearCache();
            Core.Win32Api.TrimWorkingSet();
        };
        this.AddHandler(Validation.ErrorEvent, new EventHandler<ValidationErrorEventArgs>(OnValidationError));
        // The popup is StaysOpen="True" (see its XAML comment), so it won't auto-close when the whole
        // window loses focus -- close it explicitly instead of leaving a stale flyout floating over
        // whatever the user alt-tabbed to.
        Deactivated += (_, _) => this.CloseSearchPopup();
    }

    // Called on first Loaded, and again by AppWindowManager whenever an already-open (cached, just
    // hidden) window is re-shown -- Loaded only fires once per window lifetime, so that reuse path
    // wouldn't otherwise get focus back on the search box.
    public void FocusSearchBox() => Dispatcher.BeginInvoke(new Action(() =>
    {
        // Focus doesn't reliably stick if requested before the window has actually finished
        // activating -- deferring to Background priority (same pattern QuickSearchWindow uses for its
        // own search box) waits until layout/activation settles first.
        TxtSettingsSearch.Focus();
        Keyboard.Focus(TxtSettingsSearch);
    }), System.Windows.Threading.DispatcherPriority.Background);

    private void TxtSettingsSearch_TextChanged(object sender, TextChangedEventArgs e) => this.OnSettingsSearchTextChanged();

    private void SettingsWindow_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSidebarLayout(e.NewSize.Width);

    private void UpdateSidebarLayout(double windowWidth)
    {
        IsSidebarCompact = SettingsSidebarLayout.IsCompact(windowWidth);
        SidebarColumn.Width = IsSidebarCompact ? new GridLength(SettingsSidebarLayout.CompactWidth) : GridLength.Auto;
    }

    private void TxtSettingsSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) => this.OnSettingsSearchKeyDown(e);

    private void LstSearchResults_MouseUp(object sender, MouseButtonEventArgs e) => this.OnSettingsSearchResultsMouseUp(e);

    private void OnValidationError(object? sender, ValidationErrorEventArgs e)
    {
        if (e.Action == ValidationErrorEventAction.Added)
            _validationErrorCount++;
        else
            _validationErrorCount--;

        if (DataContext is SettingsViewModel vm)
        {
            vm.CanApply = _validationErrorCount == 0;
        }
    }

    // Case-insensitive: callers may provide a section name from the host UI, SDK, or external URI
    // compatibility route.
    public void SelectSection(string tag)
    {
        if (LstSections == null || LstSectionsBottom == null)
            return;

        foreach (ListBoxItem item in LstSections.Items)
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                LstSectionsBottom.SelectedItem = null;
                LstSections.SelectedItem = item;
                return;
            }
        }
        foreach (ListBoxItem item in LstSectionsBottom.Items)
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                LstSections.SelectedItem = null;
                LstSectionsBottom.SelectedItem = item;
                return;
            }
        }
    }

    // The index is this entry's position in SettingsWindowSearchExtensions.BuildAllEntries's combined
    // static+dynamic list, the same list PluginSdk.Services.SettingsSearchService exposes to plugins
    // (see App.xaml.cs). Reuses ActivateSearchResult (the same jump+highlight a typed search-box match
    // would trigger) rather than duplicating its section/tab-selection and highlight logic.
    //
    // Passes this window's own real DataContext (needed so the Plugins/Hotkeys dynamic
    // entries' Reveal step resolves against the SAME live-bound objects actually in the visual tree --
    // otherwise ContainerFromItem never finds a match and the highlight silently no-ops), but
    // evaluateConditionalVisibility: false so the static IsVisible-gated entries (WSL tab, etc.) are
    // excluded the exact same way the SDK feed's own vm: null build always excludes them -- otherwise
    // this window's actual live state could include one of those entries where the SDK's build never
    // did, shifting every later index out of alignment (see BuildAllEntries's own comment).
    public void JumpToEntry(int index)
    {
        var entries = SettingsWindowSearchExtensions.BuildAllEntries(DataContext as SettingsViewModel, evaluateConditionalVisibility: false);
        if (index < 0 || index >= entries.Count)
            return;

        this.ActivateSearchResult(entries[index]);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        WindowMaximizedDragHelper.DragMoveOrRestore(this, e);
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // Custom chrome draws its own rounded frame with a margin for the drop shadow. Maximized, both are
    // wrong: the rounding leaves cut corners against the screen edge and the margin leaves a gap round
    // the whole window, so they are flattened while maximized and restored afterwards. Mirrors the full
    // search window's SearchWindowChromeHandler.HandleStateChanged.
    private void Window_StateChanged(object sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;

        BtnMaximize.Content = maximized ? "" : "";

        MainBorder.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(10);
        MainBorder.Margin = maximized ? new Thickness(0) : new Thickness(8);
        MainBorder.BorderThickness = new Thickness(maximized ? 0 : 1);
        ClippingBorder.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(10);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // The bottom-docked "About" entry lives in its own ListBox (see XAML comment on LstSectionsBottom) so
    // it can be pinned to the sidebar's bottom edge -- both lists feed the same page-switching logic below
    // and clear each other's selection so only one item is ever highlighted at a time.
    private void LstSections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstSections.SelectedItem == null)
            return;

        LstSectionsBottom.SelectedItem = null;
        ApplySelectedSection((LstSections.SelectedItem as ListBoxItem)?.Tag as string ?? "Service");
    }

    private void LstSectionsBottom_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstSectionsBottom.SelectedItem == null)
            return;

        LstSections.SelectedItem = null;
        ApplySelectedSection((LstSectionsBottom.SelectedItem as ListBoxItem)?.Tag as string ?? "About");
    }

    private void ApplySelectedSection(string tag)
    {
        // Covers navigating via the sidebar directly while a search popup happens to be open (typed a
        // query, then clicked a section instead of a result) -- clearing the text closes the popup too.
        TxtSettingsSearch.Text = string.Empty;

        // Only ever touches the page being left (already built, cheap to hide) and the page being
        // entered (this.GetSectionPage lazily constructs it on first visit) -- never the other, still
        // untouched tabs, which is the whole point of the lazy PageXxx properties above.
        _currentPage?.Visibility = Visibility.Collapsed;

        var page = this.GetSectionPage(tag);
        page?.Visibility = Visibility.Visible;
        _currentPage = page;
    }
}
