using System.Windows;
using System.Windows.Controls;
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

    public FileOccupationView(string path)
    {
        InitializeComponent();
        _path = path;
        PathText.Text = path;
        PathText.ToolTip = path;
        NameColumn.Header = TranslationService.Get("FileUnlocker_ProcessName");
        PidColumn.Header = TranslationService.Get("FileUnlocker_ProcessId");
        PathColumn.Header = TranslationService.Get("FileUnlocker_ProcessPath");
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

    private void PathViewport_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _pathIsHovered = true;
        UpdatePathMarquee();
    }

    private void PathViewport_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
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
            ProcessList.ItemsSource = null;
            EmptyText.Visibility = Visibility.Collapsed;
            _hasProcesses = false;
            UpdateReleaseButtonState();
            return;
        }

        ProcessList.ItemsSource = result.Processes;
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

    private void UpdateReleaseButtonState()
    {
        _releaseButton?.IsEnabled = !_busy && _hasProcesses;
    }
}
