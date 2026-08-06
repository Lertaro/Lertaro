using System.Windows;
using System.Windows.Controls;
using System.Collections;
using System.Collections.Specialized;

namespace Lertaro.App.Views.Controls.Results;

public enum ResultsViewMode
{
    List,
    Grid
}

public partial class ResultsControl : System.Windows.Controls.UserControl
{
    public ResultsControl()
    {
        InitializeComponent();
        InitializeSelectionChangedHandlers();
        ResultsDragDropHelper.Register(LstResults);
        ResultsDragDropHelper.Register(LstGridResults);

        // List mode only (quick/inline windows): hovering a row selects it, matching how Spotlight/
        // Alfred-style launchers behave. Rows with IsHitTestVisible="False" (section headers, the
        // empty-result placeholder -- see ResultItemStyle) never resolve to a ListBoxItem here, so
        // they're naturally skipped without any extra checks.
        //
        // WPF re-hit-tests a stationary mouse whenever the visual tree changes underneath it (rows
        // relaid out as results repopulate), and synthesizes a MouseMove for that even though the
        // cursor never physically moved. If it happens to now sit over row 2+ (e.g. results expanded
        // under a cursor that was resting there from a previous window position), that synthetic event
        // used to steal selection away from the row 0 default OnCollectionChanged just set. Only treat
        // a MouseMove as real hover if the coordinate actually changed since the last one seen --
        // _lastHoverPos is reseeded to wherever the cursor currently sits every time OnCollectionChanged
        // resets selection, so the first (synthetic) move after a refresh always matches and is ignored.
        LstResults.MouseMove += (s, e) =>
        {
            var pos = e.GetPosition(LstResults);
            if (_lastHoverPos.HasValue && pos == _lastHoverPos.Value) return;
            _lastHoverPos = pos;

            var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item?.Content != null && !ReferenceEquals(LstResults.SelectedItem, item.Content))
            {
                LstResults.SelectedItem = item.Content;
            }
        };
        void HandleMiddleClickPreview(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Middle)
            {
                var parentWindow = Window.GetWindow(this);
                if (parentWindow != null && parentWindow.GetType().Name != "InlineSearchWindow")
                {
                    var listBox = sender as System.Windows.Controls.ListBox;
                    var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
                    if (item?.Content is AppSearchResult result && result.CanPreview)
                    {
                        listBox?.SelectedItem = result;
                        Services.QuickLookManager.Instance.Toggle(parentWindow, result.FullPath);
                        e.Handled = true;
                    }
                }
            }
        }

        LstResults.MouseDown += HandleMiddleClickPreview;
        LstGridResults.MouseDown += HandleMiddleClickPreview;

        // Dynamically load custom GridView columns from ResultColumnProviders
        Loaded += (s, e) =>
        {
            UpdateViewModeVisibility();
            LoadDynamicColumns();
        };

        // Every grid column's header (built-in and plugin alike) can end up as a one-time translated
        // snapshot rather than a live binding -- plugin columns always are (PopulateDynamicColumns sets
        // Header as a literal string), and a built-in column's own live XAML binding is overwritten the
        // first time it's clicked/sorted (see ResultsControlColumns' own comment). Re-resolve them all on
        // every TranslationManager change so none stay stuck in whatever language was active at that
        // point; harmless no-op for List-mode-only owners (QuickSearchWindow/InlineSearchWindow) since
        // LstGridResults still exists there, just hidden. Unsubscribes on Unloaded so a closed
        // SearchWindow's ResultsControl doesn't linger forever pinned by the singleton's event -- never
        // fires for QuickSearchWindow/InlineSearchWindow's instances since those windows are only ever
        // Hidden, not Closed, which is exactly the lifetime this subscription should have there too.
        Services.TranslationManager.Instance.PropertyChanged += OnTranslationsChanged;
        Unloaded += (s, e) => Services.TranslationManager.Instance.PropertyChanged -= OnTranslationsChanged;
    }

    private void OnTranslationsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == "Item[]")
            ResultsControlColumns.RefreshAllColumnHeaders(LstGridResults);
    }

    private System.Windows.Point? _lastHoverPos;

    internal static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = child is FrameworkContentElement fce ? fce.Parent : System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    public Border LoadingBorder => null!;
    public System.Windows.Controls.Control LoadingProgressBar => null!;
    public TextBlock LoadingTitleTextBlock => null!;
    public TextBlock LoadingStatsTextBlock => null!;
    public System.Windows.Controls.Button InstallServiceButton => null!;
    public System.Windows.Controls.ListBox ResultsListBox => LstResults;
    public Grid SearchResultsGrid => GridSearchResultsContainer;
    public Grid ActionsGrid => GridActions;
    public TextBlock ActionsTargetTextBlock => TxtActionsTarget;
    public System.Windows.Controls.ListBox ActionsListBox => LstActions;

    public System.Windows.Controls.ListBox ActiveListBox => ViewMode == ResultsViewMode.Grid ? (System.Windows.Controls.ListBox)LstGridResults : LstResults;

    // ViewMode DependencyProperty
    public static readonly DependencyProperty ViewModeProperty = DependencyProperty.Register(
        nameof(ViewMode), typeof(ResultsViewMode), typeof(ResultsControl),
        new PropertyMetadata(ResultsViewMode.List, OnViewModeChanged));

    public ResultsViewMode ViewMode
    {
        get => (ResultsViewMode)GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
    }

    private static void OnViewModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ResultsControl control)
        {
            control.UpdateViewModeVisibility();
        }
    }

    private void UpdateViewModeVisibility()
    {
        if (GridSearchResults == null || GridSearchResultsGrid == null) return;
        if (ViewMode == ResultsViewMode.Grid)
        {
            GridSearchResults.Visibility = Visibility.Collapsed;
            GridSearchResultsGrid.Visibility = Visibility.Visible;
        }
        else
        {
            GridSearchResults.Visibility = Visibility.Visible;
            GridSearchResultsGrid.Visibility = Visibility.Collapsed;
        }
    }

    // ItemsSource DependencyProperty
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(ResultsControl),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public IEnumerable ItemsSource
    {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ResultsControl control)
        {
            control.UpdateItemsSource(e.OldValue as IEnumerable, e.NewValue as IEnumerable);
        }
    }

    private void UpdateItemsSource(IEnumerable? oldValue, IEnumerable? newValue)
    {
        if (oldValue is INotifyCollectionChanged oldNotify)
        {
            oldNotify.CollectionChanged -= OnCollectionChanged;
        }

        LstResults?.ItemsSource = newValue;
        LstGridResults?.ItemsSource = newValue;

        if (newValue is INotifyCollectionChanged newNotify)
        {
            newNotify.CollectionChanged += OnCollectionChanged;
        }
    }

    // ReconcileTo (ObservableRangeCollection) raises one CollectionChanged event PER CHANGED ROW, not
    // one for the whole batch -- and SearchResultsReconciler.ItemsEqual compares SearchQuery, which is
    // re-stamped with the just-typed text on every keystroke, so essentially every row differs every
    // time (needed so an already-realized row's TextHighlighter binding picks up the new query and
    // re-highlights). For the full window's 1000-item budget that's up to ~1000 individual events per
    // keystroke; without this guard, each one independently scheduled its own Dispatcher.BeginInvoke
    // below, so a single keystroke could queue up to ~1000 Render-priority callbacks (each redoing
    // SelectedIndex/ScrollIntoView) for the UI thread to drain before it could respond to the next one --
    // only the LAST of those ever did anything observable anyway, since the list had already fully
    // settled to its final state by the time any of them actually ran. Collapsing the whole burst down to
    // exactly one scheduled callback (it naturally runs after ReconcileTo's synchronous loop finishes,
    // since BeginInvoke never runs mid-loop on the same thread) keeps the exact same observable result
    // with none of the wasted intermediate work.
    private bool _collectionChangedPending;

    // Where the user last put themselves in the CURRENT result set: the row they selected and how far
    // down they scrolled. Both stay at 0 until they actually do something, so a set nobody has touched
    // restores to exactly the top-of-list state this handler has always produced.
    //
    // Needed because a Reset (which is what a large update raises -- see ObservableRangeCollection's
    // ResetInsteadOfReconcileThreshold) tells WPF that every item is gone, so Selector drops the
    // selection outright and the panel is free to drop the scroll offset with it. Nothing survives for
    // the handler below to notice, which is why the position has to be recorded as the user creates it
    // rather than read back afterwards.
    private int _anchorIndex;
    private double _anchorOffset;
    private ScrollViewer? _resultsScrollViewer;
    private bool _suppressAnchorCapture;

    private void OnResultsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer scrollViewer)
            return;
        _resultsScrollViewer = scrollViewer;

        // ExtentHeightChange separates a scroll the user performed from the offset moving because rows
        // were added or removed underneath them. Only the former is a statement about where they want
        // to be; recording the latter would let a repaint that resets the offset overwrite the very
        // position this exists to restore.
        if (!_suppressAnchorCapture && e.ExtentHeightChange == 0 && e.VerticalChange != 0)
            _anchorOffset = scrollViewer.VerticalOffset;
    }

    private void CaptureSelectionAnchor(int index)
    {
        // A deselection is never recorded. Nothing the user does produces one here, but a Reset does:
        // Selector drops the selection while handling it and raises SelectionChanged with -1, which
        // arrives before the callback below runs -- so without this guard the teardown would erase the
        // very anchor that teardown is the reason for keeping.
        if (!_suppressAnchorCapture && index >= 0)
            _anchorIndex = index;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Read here rather than in the deferred callback below only for clarity -- the flag is set
        // before the notification and outlives it -- but it does mean a burst is judged by its first
        // event, which is what the rest of this handler already assumes.
        var extendsContent = sender is Helpers.ObservableRangeCollection<AppSearchResult> { LastUpdateExtendedContent: true };

        if (_collectionChangedPending) return;
        _collectionChangedPending = true;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            _collectionChangedPending = false;

            if (GridActions != null && GridActions.Visibility == Visibility.Visible)
                return;

            var list = ActiveListBox;
            if (list == null)
                return;

            if (list.Items.Count == 0)
            {
                _anchorIndex = 0;
                _anchorOffset = 0;
                list.SelectedIndex = -1;
                return;
            }

            if (extendsContent)
            {
                // The same result set got longer. Now that a tail append updates the rows it added and
                // nothing else, the selection survives it -- and if it survived there is nothing here
                // worth doing. Reassigning it anyway is not the harmless no-op it looks like: on a
                // multi-select list it collapses the selection to one row, and it closes a context menu
                // the user has open over that selection, which is how this surfaced -- a menu that
                // dismissed itself every time more results arrived behind it.
                if (list.SelectedIndex >= 0)
                    return;

                // Selection gone, so this update went through a Reset -- a late result that outranked
                // what was already shown, reordering rather than appending. Put the user back where
                // they were rather than at the top.
                _suppressAnchorCapture = true;
                try
                {
                    list.SelectedIndex = Math.Clamp(_anchorIndex, 0, list.Items.Count - 1);
                    // Replaying the exact offset rather than ScrollIntoView, which would put the row
                    // wherever it takes least scrolling to reveal -- not where the user left it.
                    if (_anchorOffset > 0 && _resultsScrollViewer != null)
                        _resultsScrollViewer.ScrollToVerticalOffset(_anchorOffset);
                }
                finally
                {
                    _suppressAnchorCapture = false;
                }
                return;
            }

            // A different result set: whatever position the user had was a position in a list that no
            // longer exists, so it goes back to the top.
            _anchorIndex = 0;
            _anchorOffset = 0;
            _suppressAnchorCapture = true;
            try
            {
                list.SelectedIndex = 0;
                if (ViewMode == ResultsViewMode.Grid)
                    LstGridResults.ScrollIntoView(LstGridResults.SelectedItem);
                else
                    LstResults.ScrollIntoView(LstResults.SelectedItem);
            }
            finally
            {
                _suppressAnchorCapture = false;
            }

            // Reseed the hover baseline to the cursor's current spot so the MouseMove WPF synthesizes
            // once these rows finish laying out under it doesn't get mistaken for real movement (see
            // LstResults.MouseMove above).
            _lastHoverPos = System.Windows.Input.Mouse.GetPosition(LstResults);
        }), System.Windows.Threading.DispatcherPriority.Render);
    }

    // SelectedItem DependencyProperty
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(object), typeof(ResultsControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

    public object SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ResultsControl control)
        {
            control.UpdateSelectedItem(e.NewValue);
        }
    }

    private bool _isUpdatingSelection;

    private void UpdateSelectedItem(object value)
    {
        if (_isUpdatingSelection) return;
        _isUpdatingSelection = true;
        try
        {
            if (ViewMode == ResultsViewMode.Grid)
            {
                LstGridResults?.SelectedItem = value;
            }
            else
            {
                LstResults?.SelectedItem = value;
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void InitializeSelectionChangedHandlers()
    {
        LstResults.SelectionChanged += (s, e) =>
        {
            CaptureSelectionAnchor(LstResults.SelectedIndex);
            if (_isUpdatingSelection) return;
            _isUpdatingSelection = true;
            try
            {
                SelectedItem = LstResults.SelectedItem;
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        };

        LstGridResults.SelectionChanged += (s, e) =>
        {
            CaptureSelectionAnchor(LstGridResults.SelectedIndex);
            if (_isUpdatingSelection) return;
            _isUpdatingSelection = true;
            try
            {
                SelectedItem = LstGridResults.SelectedItem;
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        };

        // Attached at the ListBox/ListView because the ScrollViewer that raises this lives inside the
        // control template and isn't reachable until it has been applied -- the event bubbles out to
        // here regardless, and carries the ScrollViewer as its OriginalSource.
        LstResults.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnResultsScrollChanged));
        LstGridResults.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnResultsScrollChanged));
    }

    private bool _columnsLoaded;
    private void LoadDynamicColumns()
    {
        if (_columnsLoaded || LstGridResults == null) return;
        _columnsLoaded = true;
        ResultsControlColumns.PopulateDynamicColumns(LstGridResults);

        // Grid mode only (the full window) -- Quick/Inline windows' DataContext has no
        // CurrentSortColumn/IsSortAscending pair to read, and never show LstGridResults anyway.
        if (ViewMode == ResultsViewMode.Grid)
        {
            ResultsControlColumns.ApplyColumnOrder(LstGridResults, Core.UserSettings.Load().ColumnOrder);
            ResultsControlColumns.ApplyInitialSortIndicator(LstGridResults, DataContext);
        }
    }

    // sender is always the ListView itself (that's where GridViewColumnHeader.Click="..." attaches the
    // handler in XAML) -- WPF only walks the handler UP the tree via routing, it doesn't rewrite
    // `sender` to whatever was actually clicked. The real clicked element is e.OriginalSource, which for
    // a click on the header's own text/content is some element INSIDE the header (a ContentPresenter,
    // a TextBlock, ...), so it needs walking back up to find the enclosing GridViewColumnHeader.
    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e) =>
        ResultsControlColumns.HandleColumnHeaderClick(
            FindVisualParent<GridViewColumnHeader>(e.OriginalSource as DependencyObject), DataContext, LstGridResults);
}
