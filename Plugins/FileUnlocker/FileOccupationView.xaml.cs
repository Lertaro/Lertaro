using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Windows;

namespace Lertaro.Plugins.FileUnlocker;

public partial class FileOccupationView : UserControl
{
    private readonly string _path;
    private bool _busy;
    private bool _pathIsHovered;
    private double _pathTextWidth;
    private bool _hasProcesses;
    private Button? _releaseButton;
    private string? _sortProperty;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;
    private string _processHeader = string.Empty;
    private string _pidHeader = string.Empty;
    private string _pathHeader = string.Empty;
    private ICollectionView? _processView;

    public FileOccupationView(string path)
    {
        InitializeComponent();
        _path = path;
        PathText.Text = path;
        _processHeader = TranslationService.Get("FileUnlocker_ProcessName");
        _pidHeader = TranslationService.Get("FileUnlocker_ProcessId");
        _pathHeader = TranslationService.Get("FileUnlocker_ProcessPath");
        UpdateSortHeaders();
        EmptyText.Text = TranslationService.Get("FileUnlocker_NoProcesses");
    }

    internal void AttachFooter(PluginWindow window)
    {
        var refresh = new Button { Style = window.FindResource("SettingsButton") as Style };
        refresh.Content = TranslationService.Get("FileUnlocker_Refresh");
        refresh.Click += RefreshButton_Click;

        var release = new Button { Style = window.FindResource("PrimarySettingsButton") as Style };
        release.Content = TranslationService.Get("FileUnlocker_RequestRelease");
        release.IsDefault = true;
        release.Click += ReleaseButton_Click;
        _releaseButton = release;

        window.Footer.Children.Add(refresh);
        window.Footer.Children.Add(release);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void PathViewport_MouseEnter(object sender, MouseEventArgs e)
    {
        _pathIsHovered = true;
        UpdatePathMarquee();
    }

    private void PathViewport_MouseLeave(object sender, MouseEventArgs e)
    {
        _pathIsHovered = false;
        StopPathMarquee();
    }

    private void PathViewport_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePathMarquee();

    private void UpdatePathMarquee()
    {
        if (!_pathIsHovered || PathViewport.ActualWidth <= 0)
        {
            StopPathMarquee();
            return;
        }

        PathText.TextTrimming = TextTrimming.None;
        PathText.Width = double.NaN;
        PathText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _pathTextWidth = PathText.DesiredSize.Width;
        if (_pathTextWidth <= PathViewport.ActualWidth + 1)
        {
            StopPathMarquee();
            return;
        }

        PathText.Width = _pathTextWidth;
        var distance = _pathTextWidth - PathViewport.ActualWidth;
        var animation = new DoubleAnimation
        {
            From = 0,
            To = -distance,
            BeginTime = TimeSpan.FromMilliseconds(700),
            Duration = TimeSpan.FromMilliseconds(Math.Max(1800, distance * 28)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        PathTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animation);
    }

    private void StopPathMarquee()
    {
        PathTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        PathTransform.X = 0;
        PathText.Width = double.NaN;
        PathText.TextTrimming = TextTrimming.CharacterEllipsis;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void ReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        var result = await Task.Run(() => RestartManagerClient.RequestShutdown(_path));
        ApplyResult(result);
        SetBusy(false);
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        SetBusy(true);
        var result = await Task.Run(() => RestartManagerClient.Query(_path));
        ApplyResult(result);
        SetBusy(false);
    }

    private void ApplyResult(RestartManagerClient.RestartManagerResult result)
    {
        if (result.Error is not null)
        {
            StatusText.Text = string.Format(TranslationService.Get("FileUnlocker_QueryFailed"), result.Error);
            _processView = null;
            ProcessList.ItemsSource = null;
            EmptyText.Visibility = Visibility.Collapsed;
            _hasProcesses = false;
            UpdateReleaseButtonState();
            return;
        }

        _processView = new ListCollectionView(result.Processes.ToList());
        ProcessList.ItemsSource = _processView;
        ApplyCurrentSort();
        _hasProcesses = result.Processes.Count > 0;
        EmptyText.Visibility = result.Processes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = result.Processes.Count == 0
            ? TranslationService.Get("FileUnlocker_NoProcesses")
            : string.Format(TranslationService.Get("FileUnlocker_Detected"), result.Processes.Count);
        UpdateReleaseButtonState();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        StatusText.Text = busy ? TranslationService.Get("FileUnlocker_Querying") : StatusText.Text;
        UpdateReleaseButtonState();
    }

    private void UpdateReleaseButtonState() => _releaseButton?.IsEnabled = !_busy && _hasProcesses;

    private void ProcessList_ColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (FindVisualParent<GridViewColumnHeader>(e.OriginalSource as DependencyObject) is not { Column: not null } header) return;

        var property = header.Column == NameColumn ? nameof(RestartManagerClient.LockedProcess.Name)
            : header.Column == PidColumn ? nameof(RestartManagerClient.LockedProcess.ProcessId)
            : header.Column == PathColumn ? nameof(RestartManagerClient.LockedProcess.ExecutablePath)
            : null;
        if (property == null) return;

        if (string.Equals(_sortProperty, property, StringComparison.Ordinal))
            _sortDirection = _sortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        else
        {
            _sortProperty = property;
            _sortDirection = ListSortDirection.Ascending;
        }

        ApplyCurrentSort();
    }

    private void ApplyCurrentSort()
    {
        if (_processView == null) return;

        _processView.SortDescriptions.Clear();
        if (_sortProperty != null)
            _processView.SortDescriptions.Add(new SortDescription(_sortProperty, _sortDirection));
        UpdateSortHeaders();
    }

    private void UpdateSortHeaders()
    {
        NameColumn.Header = AddSortIndicator(_processHeader, nameof(RestartManagerClient.LockedProcess.Name));
        PidColumn.Header = AddSortIndicator(_pidHeader, nameof(RestartManagerClient.LockedProcess.ProcessId));
        PathColumn.Header = AddSortIndicator(_pathHeader, nameof(RestartManagerClient.LockedProcess.ExecutablePath));
    }

    private string AddSortIndicator(string header, string property)
    {
        if (!string.Equals(_sortProperty, property, StringComparison.Ordinal)) return header;
        return header + (_sortDirection == ListSortDirection.Ascending ? " ▲" : " ▼");
    }

    private void ProcessList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Shift) return;
        if (FindScrollViewer(ProcessList) is not { } scrollViewer) return;

        if (e.Delta > 0) scrollViewer.LineLeft();
        else scrollViewer.LineRight();
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScrollViewer(System.Windows.Media.VisualTreeHelper.GetChild(root, i)) is { } childViewer)
                return childViewer;
        }

        return null;
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        for (var current = child; current != null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is T match) return match;
        }

        return null;
    }
}
