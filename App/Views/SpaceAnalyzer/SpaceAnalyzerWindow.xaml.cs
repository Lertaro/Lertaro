using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services;
using Lertaro.App.Services.ShellMenu.ActionFlyout;
using Lertaro.App.Services.Theme;
using Lertaro.App.ViewModels.SpaceAnalyzer;
using Lertaro.Core.IndexV2.Space;
using Lertaro.PluginSdk.Abstractions;
using Button = System.Windows.Controls.Button;

namespace Lertaro.App.Views.SpaceAnalyzer;

public partial class SpaceAnalyzerWindow : Window, IPluginSearchWindow
{
    private readonly List<Location> _history = [];
    private IReadOnlyList<SpaceDisplayItem> _items = Array.Empty<SpaceDisplayItem>();
    private IndexedSpaceCatalog? _catalog;
    private CancellationTokenSource? _loadCts;
    private bool _isLoading;
    private bool _loadFailed;

    public SpaceAnalyzerWindow()
    {
        InitializeComponent();
        SystemMenuBlocker.Attach(this, blockClose: false);
        MaximizeBoundsHelper.Attach(this);
        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo, this);
        ThemeManager.Instance.ThemeChanged += OnThemeChanged;
        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
        Loaded += async (_, _) => await ReloadAsync();
        Closed += OnClosed;
        StateChanged += Window_StateChanged;
    }

    private async Task ReloadAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        _loadFailed = false;
        SetLoading(true);

        IndexedSpaceCatalog? fresh = null;
        try
        {
            fresh = await Task.Run(IndexedSpaceCatalog.OpenDefault, token);
            token.ThrowIfCancellationRequested();
            var old = _catalog;
            _catalog = fresh;
            fresh = null;
            old?.Dispose();
            _history.Clear();
            _history.Add(new Location(null, -1, TranslationManager.Instance["Space_Home"]));
            ShowCurrentLocation();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[SpaceAnalyzer] Failed to load index caches: {ex.Message}", Core.LogLevel.Error);
            _loadFailed = true;
            _items = Array.Empty<SpaceDisplayItem>();
            ItemsList.ItemsSource = _items;
        }
        finally
        {
            fresh?.Dispose();
            if (!token.IsCancellationRequested)
                SetLoading(false);
        }
    }

    private void ShowCurrentLocation()
    {
        if (_catalog == null || _history.Count == 0)
            return;
        var location = _history[^1];
        _items = location.Source == null
            ? _catalog.Sources.Select(source => new SpaceDisplayItem { Source = source, Entry = source.Root }).ToList()
            : location.Source.GetChildren(location.Row)
                .Select(entry => new SpaceDisplayItem { Source = location.Source, Entry = entry }).ToList();
        ItemsList.ItemsSource = _items;
        ItemsList.SelectedItem = null;
        BackButton.IsEnabled = _history.Count > 1;
        RebuildBreadcrumbs();
        UpdateEmptyState();
        UpdateSummary();
        RenderTreemap();
    }

    private void NavigateTo(SpaceDisplayItem item)
    {
        if (!item.IsDirectory)
            return;
        _history.Add(new Location(item.Source, item.Entry.Row, item.Name));
        ShowCurrentLocation();
    }

    private void NavigateToHistory(int index)
    {
        if ((uint)index >= (uint)_history.Count)
            return;
        _history.RemoveRange(index + 1, _history.Count - index - 1);
        ShowCurrentLocation();
    }

    private void RebuildBreadcrumbs()
    {
        BreadcrumbPanel.Children.Clear();
        for (var index = 0; index < _history.Count; index++)
        {
            if (index > 0)
            {
                BreadcrumbPanel.Children.Add(new TextBlock
                {
                    Text = "\uE76C",
                    FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                    FontSize = 9,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(1, 0, 1, 0)
                });
            }
            var target = index;
            var isCurrent = index == _history.Count - 1;
            var button = new Button
            {
                Content = _history[index].Name,
                Style = (Style)FindResource(isCurrent ? "SpaceCurrentBreadcrumbButton" : "SpaceBreadcrumbButton"),
                IsHitTestVisible = !isCurrent
            };
            if (!isCurrent)
                button.Click += (_, _) => NavigateToHistory(target);
            BreadcrumbPanel.Children.Add(button);
        }
    }

    private void RenderTreemap()
        => SpaceTreemapPresenter.Render(TreemapCanvas, _items, ItemsList.SelectedItem as SpaceDisplayItem,
            item => ItemsList.SelectedItem = item, NavigateTo, ShowActions);

    private void ShowActions(SpaceDisplayItem item)
    {
        var fullPath = item.Source.GetPath(item.Entry.Row);
        var result = new AppSearchResult
        {
            Name = item.Name,
            FullPath = fullPath,
            ParentDir = System.IO.Path.GetDirectoryName(fullPath) ?? string.Empty,
            IsDir = item.IsDirectory,
            Drive = (System.IO.Path.GetPathRoot(fullPath) ?? string.Empty).TrimEnd('\\', '/'),
            ResultKind = "File"
        };
        ActionFlyout.Show([result], this, this, TreemapCanvas, PlacementMode.MousePoint);
    }

    public void OpenFileOrFolderExternal(string path) => FileExecutor.OpenFileOrFolder(path);
    public void OpenFileOrFolderAsAdminExternal(string path) => FileExecutor.OpenFileOrFolderAsAdmin(path);
    public void LocateInExplorerExternal(string path) => FileExecutor.LocateInExplorer(path);
    public void HideWindow() => Close();

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = _isLoading || _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_isLoading)
            EmptyText.Text = TranslationManager.Instance["Space_Loading"];
        else if (_loadFailed)
            EmptyText.Text = TranslationManager.Instance["Space_LoadFailed"];
        else if (_catalog?.Sources.Count == 0)
            EmptyText.Text = TranslationManager.Instance["Space_NoIndexes"];
        else if (_items.Count == 0)
            EmptyText.Text = TranslationManager.Instance["Space_Empty"];
    }

    private void UpdateSummary()
    {
        var total = _items.Aggregate(0L, static (sum, item) => sum > long.MaxValue - item.Size ? long.MaxValue : sum + item.Size);
        SummaryText.Text = string.Format(TranslationManager.Instance["Space_Summary"], _items.Count, SpaceSizeFormatter.Format(total));
    }

    private void SetLoading(bool loading)
    {
        _isLoading = loading;
        BackButton.IsEnabled = !loading && _history.Count > 1;
        ItemsList.IsEnabled = !loading;
        UpdateEmptyState();
    }

    private void OnThemeChanged()
    {
        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo, this);
        RenderTreemap();
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_history.Count > 0)
            _history[0] = _history[0] with { Name = TranslationManager.Instance["Space_Home"] };
        ItemsList.Items.Refresh();
        RebuildBreadcrumbs();
        UpdateEmptyState();
        UpdateSummary();
        RenderTreemap();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _catalog?.Dispose();
        ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
        TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
    }

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RenderTreemap();
    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        if (ItemsList.SelectedItem is SpaceDisplayItem item)
            NavigateTo(item);
    }
    private void ItemsList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var row = TreeWalk.Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (row?.Content is not SpaceDisplayItem item)
            return;
        ItemsList.SelectedItem = item;
        ShowActions(item);
        e.Handled = true;
    }
    private void TreemapCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderTreemap();
    private void Back_Click(object sender, RoutedEventArgs e) => NavigateToHistory(_history.Count - 2);
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;
        if (e.ClickCount == 2)
        {
            Maximize_Click(sender, e);
            return;
        }
        WindowMaximizedDragHelper.DragMoveOrRestore(this, e);
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MainBorder.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(12);
        MainBorder.Margin = maximized ? new Thickness(0) : new Thickness(8);
        MainBorder.BorderThickness = new Thickness(maximized ? 0 : 1);
        ClippingBorder.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(12);
    }

    private readonly record struct Location(IndexedSpaceSource? Source, int Row, string Name);
}
