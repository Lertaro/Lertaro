using System.Windows;
using System.Windows.Input;
using Lertaro.App.Services;
using Lertaro.App.Services.ShellMenu.ActionFlyout;
using Lertaro.App.ViewModels.QuickPanel;

namespace Lertaro.App.Views.QuickPanel;

// The panel itself: one workspace's sources, docked to the bottom-right of whatever window is in
// front. Lifecycle, the hotkey and the docking maths all live in QuickPanelManager, and what is shown
// comes from QuickPanelViewModel; this file is only the window's own input handling.
public partial class QuickPanelWindow : Window,
    PluginSdk.Abstractions.IPluginSearchWindow,
    Services.AppWindow.IHasVisibleContentInset
{
    // Must match QuickPanelWindow.xaml's root Border Margin, which is the room its drop shadow needs.
    // Without this the preview would dock against the transparent bounds and sit a shadow's width too
    // far out -- which for a window parked in a corner is the difference between the right side fitting
    // the preview and not.
    public Thickness VisibleContentInset => new(8);

    // The four methods ActionFlyout needs from whoever hosts it. Deliberately IPluginSearchWindow and
    // not ISearchWindow: that larger interface is built around an in-window actions pane, which is what
    // ShellMenuPresenter drives and what this panel has no equivalent of. The flyout asks only for these.
    public void LocateInExplorerExternal(string path) => FileExecutor.LocateInExplorer(path);
    public void OpenFileOrFolderExternal(string path) => FileExecutor.OpenFileOrFolder(path);
    public void OpenFileOrFolderAsAdminExternal(string path) => FileExecutor.OpenFileOrFolderAsAdmin(path);
    public void HideWindow() => Services.QuickPanel.QuickPanelManager.Instance?.Hide();

    public QuickPanelWindow(QuickPanelViewModel viewModel)
    {
        InitializeComponent();
        var scrollSupport = new QuickPanelScrollSupport(GroupsScrollViewer, StickyGroupHeader);
        Loaded += (_, _) => Dispatcher.BeginInvoke(scrollSupport.Update, System.Windows.Threading.DispatcherPriority.Loaded);
        Helpers.Visuals.SystemMenuBlocker.Attach(this);
        DataContext = viewModel;

        void OnViewModelChanged(object? _, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(QuickPanelViewModel.SearchQuery))
            {
                Dispatcher.BeginInvoke(new Action(SelectFirstResult), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            if (e.PropertyName == nameof(QuickPanelViewModel.HasTabStrip) && !viewModel.HasTabStrip)
                Dispatcher.BeginInvoke(new Action(() => Services.QuickPanel.QuickPanelManager.Instance?.Hide()));
        }

        viewModel.PropertyChanged += OnViewModelChanged;
        Closed += (_, _) =>
        {
            viewModel.PropertyChanged -= OnViewModelChanged;
            ReleasePreview();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Hide from Alt+Tab switcher by setting WS_EX_TOOLWINDOW style on HWND
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var exStyle = InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.GetWindowLongPtr(hwnd, InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.GWL_EXSTYLE);
            var newExStyle = new IntPtr(exStyle.ToInt64() | InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.WS_EX_TOOLWINDOW);
            InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.SetWindowLongPtr(hwnd, InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.GWL_EXSTYLE, newExStyle);
            AttachPathRequestHandler(hwnd);
        }
    }

    /// <summary>Takes the foreground and puts keyboard focus in the filter box.</summary>
    /// <remarks>
    /// The quick window's own summon, step for step, rather than a second way of doing this: the
    /// overlay dismissal has already run in QuickPanelManager.Toggle (it has to, before anything is
    /// shown), and what is left is ForceForeground through the hook, then Activate, then the box.
    ///
    /// ForceForeground rather than a bare Activate: this window is ShowActivated="False", so it comes up
    /// without focus, and Windows ignores a foreground grab from a thread that does not already own it.
    /// The hook process does own real recent input, which is what makes the handover land -- the same
    /// route and the same helper the quick window goes through.
    ///
    /// Queued at Input priority so it runs after the layout and render that Show just scheduled; asking
    /// for focus before the box exists on screen is asking a control that is not there yet.
    ///
    /// The box rather than a list: the panel is summoned to reach something, and typing towards it is
    /// the fastest way in when the workspace holds more than a screenful. What it costs is that a bare
    /// key is now typing rather than running an action -- TryRunActionHotkey stands down while the box
    /// has focus, by design, and a click on a row is what hands the keyboard back to the list.
    /// </remarks>
    public void ActivateAndFocus() => Dispatcher.BeginInvoke(new Action(() =>
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            QuickSearchWindow.Helpers.QuickSearchWindowController.ForceForeground(hwnd);

        Activate();
        Focus();
        FilterBox.FocusInput();

        // With the keyboard in the box, the first entry is what Enter would open -- so it is selected
        // from the start rather than only once something has been typed or clicked.
        SelectFirstResult();
    }), System.Windows.Threading.DispatcherPriority.Input);


    // Escape does what losing the foreground does. Previewed rather than handled on the way up, since
    // the list has focus and would consume the key first.
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Enter opens the selection, which is what a double-click does -- the filter box has the keyboard
        // after a summon and does not want Enter for anything, so the whole gesture is "type, then
        // press Enter" without ever leaving it.
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (_activeList?.SelectedItem is AppSearchResult)
            {
                e.Handled = true;
                OpenSelected();
            }
            return;
        }

        if (e.Key != Key.Escape)
        {
            // The preview, on the same key the search windows use for it -- read from the settings, so
            // rebinding it there rebinds it here. Ahead of the action hotkeys for the same reason the pin
            // is: the filter box has the keyboard after a summon, and this has to work from there.
            if (Helpers.SearchInputHelper.IsQuickLookKey(e))
            {
                if (TogglePreview()) e.Handled = true;
                return;
            }

            if (TrySwitchWorkspace(e)) return;

            // Ahead of the action hotkeys and unguarded by focus: the same key the quick window uses for
            // this, and it has to work while the filter box has the keyboard, which is where a summon
            // leaves it.
            if (Helpers.WpfUiHelper.MatchesHotkey(
                    Core.UserSettings.Load().Hotkeys.StayOpenHotkey, Keyboard.Modifiers, e.Key))
            {
                e.Handled = true;
                ToggleStayOpen();
                return;
            }

            TryRunActionHotkey(e);

            // Last, so an action bound to one of these keys wins -- the order the quick window's own
            // handler puts them in. Nothing above claims a bare arrow, and the workspace switch only
            // claims its modifier plus a digit, so what reaches here is what nobody else wanted.
            if (!e.Handled) HandleSelectionKeys(e);
            return;
        }


        // The flyout closes on its own Escape and hangs its handler on this window too. Letting this
        // one act as well would dismiss the panel out from under a menu the user was only closing.
        if (ActionFlyout.IsOpen) return;

        // A filter in the box is undone first, and only a second Escape closes the panel. Anything else
        // means the one key that gets you out of a narrowed list also throws away the panel you were
        // narrowing it in.
        if (DataContext is QuickPanelViewModel { SearchQuery.Length: > 0 } filtered)
        {
            e.Handled = true;
            filtered.SearchQuery = string.Empty;
            return;
        }

        e.Handled = true;
        Services.QuickPanel.QuickPanelManager.Instance?.Hide();
    }


    /// <summary>True while DragMove's modal loop is running, so the dismiss-on-deactivate can stand down.</summary>
    /// <remarks>
    /// DragMove blocks in a modal move loop and the window comes out of it deactivated, which the
    /// manager treats as "the user clicked away" and hides on. So dragging the panel made it vanish the
    /// moment the button came up. The flag spans the loop, and the window asks for the foreground back
    /// afterwards, since it genuinely did lose it.
    /// </remarks>
    public bool IsDraggingWindow { get; private set; }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        try
        {
            IsDraggingWindow = true;
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The guard every other custom-chrome window here carries: DragMove throws if the button
            // has already been released by the time it runs.
        }
        finally
        {
            IsDraggingWindow = false;
            Activate();
        }
    }

    // The same flyout the full window shows, anchored to the list rather than to a search box, this
    // panel having none. Right button UP rather than down, matching the full window: pressing down is
    // what moves the selection, so acting on the release is what lets a right-click on an unselected row
    // act on that row instead of on whatever was selected before it.
    private void ItemsList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox list) return;

        _activeList = list;
        var selection = list.SelectedItems.OfType<AppSearchResult>().ToList();
        if (selection.Count == 0) return;

        e.Handled = true;
        ActionFlyout.Show(selection, this, this, list, System.Windows.Controls.Primitives.PlacementMode.MousePoint);
    }

    /// <summary>The list the user last touched, which is what a keystroke acts on.</summary>
    /// <remarks>
    /// There is no single list any more: each folder group renders its own. A hotkey has to act on the
    /// one being used, and "last interacted with" is what that means in a panel where any of them can
    /// hold a selection.
    /// </remarks>
    private System.Windows.Controls.ListBox? _activeList;

    private void GroupList_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox list) return;

        Controls.Results.ResultsDragDropHelper.Register(list);
        list.SelectionChanged += GroupList_SelectionChanged;
        _activeList ??= list;
    }

    /// <summary>Hands the wheel to the scroller around the groups.</summary>
    /// <remarks>
    /// A ListBox swallows the wheel even with its own scrolling disabled, and there is one of these per
    /// folder, so the pointer resting on any group left the panel unable to scroll at all. Nothing
    /// bubbles out on its own to fix that: the event has to be re-raised at the parent by hand. The
    /// plugin config page carries the same handler for the same reason.
    /// </remarks>
    private void GroupList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox list) return;

        e.Handled = true;
        var bubbled = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = sender,
        };
        (list.Parent as UIElement)?.RaiseEvent(bubbled);
    }

    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Control.MouseDoubleClickEvent fires for ANY button's double-click (left, right, or middle) --
        // guard so double-right-clicking only opens the action flyout without launching the file.
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is not System.Windows.Controls.ListBox list) return;

        _activeList = list;
        OpenSelected();
    }

    private void ItemsList_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox list) return;

        if (e.ChangedButton == MouseButton.Middle)
        {
            _activeList = list;
            if (TogglePreview()) e.Handled = true;
        }
    }
}
