using System.Windows;
using System.Windows.Controls;
using System.Collections;

namespace Lertaro.App.Views.Controls.Results;

public enum ResultsViewMode
{
    List,
    Grid
}

public partial class ResultsControl : System.Windows.Controls.UserControl
{
    private readonly ResultsHoverSelection _hoverSelection;
    private readonly ResultsCollectionSelectionSupport _collectionSelection;

    public ResultsControl()
    {
        InitializeComponent();
        _hoverSelection = new ResultsHoverSelection(LstResults);
        _collectionSelection = new ResultsCollectionSelectionSupport(this, _hoverSelection);
        InitializeSelectionChangedHandlers();
        ResultsDragDropHelper.Register(LstResults);
        ResultsDragDropHelper.Register(LstGridResults);
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

    internal static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = child is FrameworkContentElement fce ? fce.Parent : System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    public System.Windows.Controls.ListBox ResultsListBox => LstResults;
    public Grid SearchResultsGrid => GridSearchResultsContainer;
    public Grid ActionsGrid => GridActions;
    public UIElement ActionsFlyoutHost => ActionsFlyoutBorder;
    public TextBlock ActionsTargetTextBlock => TxtActionsTarget;
    public System.Windows.Controls.ListBox ActionsListBox => LstActions;
    public System.Windows.Controls.TextBox ActionsSearchTextBox => TxtActionsSearch;
    public bool UseRoomyActionRows { get; set; }

    public static readonly DependencyProperty UsesFloatingActionsLayoutProperty = DependencyProperty.Register(
        nameof(UsesFloatingActionsLayout), typeof(bool), typeof(ResultsControl),
        new PropertyMetadata(false, OnUsesFloatingActionsLayoutChanged));

    public bool UsesFloatingActionsLayout
    {
        get => (bool)GetValue(UsesFloatingActionsLayoutProperty);
        set => SetValue(UsesFloatingActionsLayoutProperty, value);
    }

    private static void OnUsesFloatingActionsLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ResultsControl control)
            control.UpdateActionsLayoutMode();
    }

    private void UpdateActionsLayoutMode()
    {
        var floating = UsesFloatingActionsLayout;
        ActionsSearchRow.Visibility = floating ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(ActionsFlyoutBorder, floating ? 1 : 0);
        Grid.SetRow(ActionsFlyoutBorder, floating ? 1 : 0);
        Grid.SetColumnSpan(ActionsFlyoutBorder, floating ? 1 : 2);
        Grid.SetRowSpan(ActionsFlyoutBorder, floating ? 1 : 2);

        if (floating)
        {
            ActionsFlyoutBorder.Margin = new Thickness(8);
            ActionsFlyoutBorder.CornerRadius = new CornerRadius(8);
            ActionsClippingBorder.CornerRadius = new CornerRadius(8);
            ActionsFlyoutBorder.BorderThickness = new Thickness(1);
            ActionsFlyoutBorder.SetResourceReference(Border.BackgroundProperty, "CardBackground");
            ActionsFlyoutBorder.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        }
        else
        {
            ActionsFlyoutBorder.Margin = new Thickness(0);
            ActionsFlyoutBorder.CornerRadius = new CornerRadius(0);
            ActionsClippingBorder.CornerRadius = new CornerRadius(0);
            ActionsFlyoutBorder.BorderThickness = new Thickness(0);
            ActionsFlyoutBorder.Background = System.Windows.Media.Brushes.Transparent;
            ActionsFlyoutBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
            ActionsFlyoutBorder.Effect = null;
        }
    }

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
        => _collectionSelection.UpdateItemsSource(oldValue, newValue);

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
            _collectionSelection.CaptureSelectionAnchor(LstResults.SelectedIndex);
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
            _collectionSelection.CaptureSelectionAnchor(LstGridResults.SelectedIndex);
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
