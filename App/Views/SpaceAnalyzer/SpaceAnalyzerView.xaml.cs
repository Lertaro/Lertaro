using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services;
using Lertaro.App.Services.ShellMenu.ActionFlyout;
using Lertaro.App.Services.ShellIcons;
using Lertaro.App.Services.Theme;
using Lertaro.App.ViewModels.SpaceAnalyzer;
using Lertaro.Core.Services.Search;
using Lertaro.PluginSdk.Abstractions;
using Button = System.Windows.Controls.Button;
using UserControl = System.Windows.Controls.UserControl;
namespace Lertaro.App.Views.SpaceAnalyzer;
public partial class SpaceAnalyzerView : UserControl, IDisposable
{
    private readonly List<SpaceAnalyzerLocation> _history = [];
    private readonly SearchService _searchService = new();
    private readonly SpaceAnalyzerRefreshWatcher _refreshWatcher;
    private readonly SpaceAnalyzerPreviewSupport _previewSupport;
    private IReadOnlyList<SpaceDisplayItem> _items = Array.Empty<SpaceDisplayItem>();
    private CancellationTokenSource? _loadCts;
    private bool _isLoading;
    private bool _loadFailed;
    private bool _initialized;
    private bool _disposed;
    public SpaceAnalyzerView()
    {
        InitializeComponent();
        _refreshWatcher = new SpaceAnalyzerRefreshWatcher(Dispatcher, ReloadFromEventAsync, ValidateLocationFromEventAsync);
        _previewSupport = new SpaceAnalyzerPreviewSupport(this, () => (ItemsList.SelectedItem as SpaceDisplayItem)?.Path);
        SpaceAnalyzerMiddleClick.Attach(ItemsList);
        ThemeManager.Instance.ThemeChanged += OnThemeChanged;
        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
        IsVisibleChanged += OnIsVisibleChanged;
        Unloaded += (_, _) => Dispose();
    }
    private async void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_disposed)
            return;
        if (!IsVisible)
        {
            _loadCts?.Cancel();
            _refreshWatcher.Pause();
            _previewSupport.SetVisible(false);
            ActionFlyout.Close();
            return;
        }
        if (!_initialized)
        {
            _initialized = true;
            _history.Add(new SpaceAnalyzerLocation(null, TranslationManager.Instance["Space_Home"]));
            _refreshWatcher.Watch(_history.Select(location => location.Path).ToList());
            await ReloadAsync();
            return;
        }
        ItemsList.SelectedItem = null;
        if (_history.Count > 1)
            _history.RemoveRange(1, _history.Count - 1);
        _refreshWatcher.Watch(_history.Select(location => location.Path).ToList());
        _previewSupport.SetVisible(true);
        await ReloadAsync();
    }
    private Task ReloadFromEventAsync() => _isLoading || !IsVisible ? Task.CompletedTask : ReloadAsync(background: true);
    private Task ValidateLocationFromEventAsync() => _isLoading || !IsVisible ? Task.CompletedTask : ReloadAsync(true, true);
    private async Task ReloadAsync(bool background = false, bool onlyIfLocationUnavailable = false)
    {
        var selectedPath = background ? (ItemsList.SelectedItem as SpaceDisplayItem)?.Path : null;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        if (!background)
        {
            _loadFailed = false;
            SetLoading(true);
        }
        try
        {
            var locationChanged = background && await SpaceAnalyzerLocationResolver.TrimUnavailableAsync(
                _history, _searchService.GetSpaceEntriesAsync, token);
            if (onlyIfLocationUnavailable && !locationChanged)
                return;
            var entries = await _searchService.GetSpaceEntriesAsync(_history[^1].Path, token);
            token.ThrowIfCancellationRequested();
            var totalSize = entries.Aggregate(0L, static (sum, entry) => sum > long.MaxValue - entry.Size ? long.MaxValue : sum + entry.Size);
            _items = entries.Select(entry => new SpaceDisplayItem
            {
                Entry = entry,
                RelativePercentage = SpaceSizeFormatter.RelativePercentage(entry.Size, totalSize)
            }).ToList();
            ShowCurrentLocation(selectedPath);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[SpaceAnalyzer] Failed to query live indexes: {ex.Message}", Core.LogLevel.Error);
            if (background)
                return;
            _loadFailed = true;
            _items = Array.Empty<SpaceDisplayItem>();
            ItemsList.ItemsSource = _items;
        }
        finally
        {
            if (!background && !token.IsCancellationRequested)
                SetLoading(false);
        }
    }
    private void ShowCurrentLocation(string? selectedPath = null)
    {
        if (_history.Count == 0)
            return;
        ItemsList.ItemsSource = _items;
        ItemsList.SelectedItem = selectedPath == null
            ? null
            : _items.FirstOrDefault(item => string.Equals(item.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
        var canGoBack = _history.Count > 1;
        BackButton.IsEnabled = canGoBack;
        ParentListButton.Visibility = canGoBack ? Visibility.Visible : Visibility.Collapsed;
        RebuildBreadcrumbs();
        UpdateEmptyState();
        UpdateSummary();
        RenderTreemap();
        if (IsVisible)
            _refreshWatcher.Watch(_history.Select(location => location.Path).ToList());
    }
    private async void ActivateItem(SpaceDisplayItem item)
    {
        if (!item.IsDirectory)
        {
            FileExecutor.LocateInExplorer(item.Path);
            return;
        }
        ItemsList.SelectedItem = null;
        _history.Add(new SpaceAnalyzerLocation(item.Path, item.Name));
        await ReloadAsync();
    }
    private async void NavigateToHistory(int index)
    {
        if ((uint)index >= (uint)_history.Count)
            return;
        ItemsList.SelectedItem = null;
        _history.RemoveRange(index + 1, _history.Count - index - 1);
        await ReloadAsync();
    }

    private void RebuildBreadcrumbs()
    {
        BreadcrumbPanel.Children.Clear();
        for (var index = 0; index < _history.Count; index++)
        {
            if (index > 0)
                BreadcrumbPanel.Children.Add(SpaceAnalyzerBreadcrumbFactory.Create(this));
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
            SelectFromTreemap, NavigateFromTreemap, ShowActions);

    private void SelectFromTreemap(SpaceDisplayItem item)
    {
        ItemsList.SelectedItem = item;
        ItemsList.ScrollIntoView(item);
    }

    private void NavigateFromTreemap(SpaceDisplayItem item)
    {
        if (item.IsDirectory)
            TreemapCanvas.CaptureMouse();
        ActivateItem(item);
    }

    private void ShowActions(SpaceDisplayItem item)
    {
        if (Window.GetWindow(this) is not Window owner || owner is not IPluginSearchWindow view)
            return;
        var fullPath = item.Path;
        var result = new AppSearchResult
        {
            Name = item.Name,
            FullPath = fullPath,
            ParentDir = System.IO.Path.GetDirectoryName(fullPath) ?? string.Empty,
            IsDir = item.IsDirectory,
            Drive = (System.IO.Path.GetPathRoot(fullPath) ?? string.Empty).TrimEnd('\\', '/'),
            ResultKind = "File"
        };
        ActionFlyout.Show([result], view, owner, TreemapCanvas, PlacementMode.MousePoint);
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = _isLoading || _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_isLoading)
            EmptyText.Text = TranslationManager.Instance["Space_Loading"];
        else if (_loadFailed)
            EmptyText.Text = TranslationManager.Instance["Space_LoadFailed"];
        else if (_history.Count == 1 && _items.Count == 0)
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
        ParentListButton.IsEnabled = !loading && _history.Count > 1;
        ItemsList.IsEnabled = !loading;
        UpdateEmptyState();
    }

    private void OnThemeChanged() => RenderTreemap();

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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _refreshWatcher.Dispose();
        _previewSupport.Dispose();
        _searchService.Dispose();
        ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
        TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
        IsVisibleChanged -= OnIsVisibleChanged;
        ItemsList.ItemsSource = null;
        TreemapCanvas.Children.Clear();
        _items = Array.Empty<SpaceDisplayItem>();
        _history.Clear();
        ShellIconHelper.ClearCache();
        PathCacheMaintenance.ClearAllPathCaches();
        Core.Win32Api.TrimWorkingSet();
    }

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderTreemap();
        _previewSupport.SelectionChanged((ItemsList.SelectedItem as SpaceDisplayItem)?.Path);
    }
    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && ItemsList.SelectedItem is SpaceDisplayItem item)
            ActivateItem(item);
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
    private void TreemapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!TreemapCanvas.IsMouseCaptured)
            return;
        TreemapCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }
    private void Back_Click(object sender, RoutedEventArgs e) => NavigateToHistory(_history.Count - 2);
}
