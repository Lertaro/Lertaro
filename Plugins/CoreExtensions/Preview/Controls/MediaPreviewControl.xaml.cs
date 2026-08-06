using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Abstractions.Plugins.Preview;
using Lertaro.Plugins.CoreExtensions.Preview.Providers;

namespace Lertaro.Plugins.CoreExtensions.Preview.Controls;

// Auto-plays whatever file it's given (WPF's own MediaElement, no third-party codec dependency) and
// stops as soon as the QuickLook host tears it down -- see QuickLookWindow.ReleasePreview, which disposes
// ContentArea.Content when the user navigates to a different result or the window hides. Reused in place
// across consecutive media files via IReusablePreview instead of being rebuilt from scratch each time.
public partial class MediaPreviewControl : UserControl, IDisposable, IReusablePreview
{
    // internal (not private): MediaPreviewControlIconTests parses these directly, so a typo in the path
    // mini-language fails a test instead of only surfacing the first time a user actually opens a file.
    internal const string PlayIconData = "M6,4 L20,12 L6,20 Z";
    internal const string PauseIconData = "M6,4 L10,4 L10,20 L6,20 Z M14,4 L18,4 L18,20 L14,20 Z";
    internal const string VolumeIconData = "M3,10 L7,10 L12,5 L12,19 L7,14 L3,14 Z M15,7 A7,7 0 0 1 15,17";
    internal const string MutedIconData = "M3,10 L7,10 L12,5 L12,19 L7,14 L3,14 Z M15,7 L21,17 M21,7 L15,17";

    // MediaElement.Position is a real seek on the underlying decoder, not a cheap property set --
    // MouseMove fires far faster than the decoder can keep up with during a drag, so issuing one per
    // event floods it with seek requests that queue up behind each other and play back as a stutter of
    // small jumps instead of a smooth scrub. Throttling how often a seek actually commits (while still
    // updating the visual fill/time label on every move, so the drag still feels like it's tracking the
    // cursor) fixes that; MouseUp always force-commits whatever the last position was, so a throttled-
    // away seek never leaves the player short of wherever the user actually released.
    private static readonly TimeSpan ScrubSeekThrottle = TimeSpan.FromMilliseconds(120);

    private readonly DispatcherTimer _positionTimer;
    private string _currentPath = string.Empty;
    private bool _isPlaying;
    private bool _isDraggingScrub;
    private TimeSpan _duration;
    private DateTime _lastScrubSeekTime = DateTime.MinValue;
    private TimeSpan? _pendingScrubTarget;

    public MediaPreviewControl(string path)
    {
        InitializeComponent();

        Player.MediaOpened += Player_MediaOpened;
        Player.MediaEnded += Player_MediaEnded;
        Player.MediaFailed += Player_MediaFailed;

        IconMute.Data = Geometry.Parse(VolumeIconData);

        _positionTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(200) };
        _positionTimer.Tick += (_, _) => UpdateProgress();

        LoadAndPlay(path);
    }

    private void LoadAndPlay(string path)
    {
        _currentPath = path;
        Player.LayoutTransform = Transform.Identity; // control is reused across files (IReusablePreview): clear any rotation left from the previous one
        try
        {
            Player.Source = new Uri(path);
            Player.Volume = 1;
            Player.Play();
            _isPlaying = true;
            _positionTimer.Start();
            UpdatePlayPauseIcon();
        }
        catch
        {
            ShowFailure();
        }
    }

    public bool TrySetTarget(string path, bool isDir)
    {
        if (isDir || !MediaPreviewProvider.IsSupportedExtension(Path.GetExtension(path)))
            return false;

        _positionTimer.Stop();
        try { Player.Stop(); } catch { }
        AudioPlaceholder.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        Player.Visibility = Visibility.Visible;
        ScrubFill.Width = 0;
        TxtCurrentTime.Text = "0:00";
        TxtDuration.Text = "0:00";

        LoadAndPlay(path);
        return true;
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        AudioPlaceholder.Visibility = Player.HasVideo ? Visibility.Collapsed : Visibility.Visible;
        if (!Player.HasVideo) TxtAudioTitle.Text = Path.GetFileName(_currentPath);

        _duration = Player.NaturalDuration.HasTimeSpan ? Player.NaturalDuration.TimeSpan : TimeSpan.Zero;
        TxtDuration.Text = FormatTime(_duration);

        if (Player.HasVideo) ApplyRotation(_currentPath);
    }

    // Reading the MP4/MOV rotation matrix is file I/O (see Mp4RotationReader), so it's kept off the UI
    // thread; the _currentPath re-check on return guards against the control having been reused for a
    // different file (IReusablePreview) by the time this completes, same pattern ShowFailure already uses.
    private void ApplyRotation(string path) => Task.Run(() => Mp4RotationReader.GetRotationDegrees(path)).ContinueWith(t =>
                                                    {
                                                        if (_currentPath != path || t.Status != TaskStatus.RanToCompletion) return;
                                                        Player.LayoutTransform = t.Result == 0 ? Transform.Identity : new RotateTransform(t.Result);
                                                    }, TaskScheduler.FromCurrentSynchronizationContext());

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        _isPlaying = false;
        UpdatePlayPauseIcon();
        Player.Position = TimeSpan.Zero;
        Player.Pause();
        UpdateProgress();
    }

    private void Player_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _positionTimer.Stop();
        ShowFailure();
    }

    // MediaFailed only means MediaElement itself can't decode this file -- the shell can often still
    // produce a thumbnail (its own decoder pipeline, e.g. an unsupported audio codec inside an otherwise
    // fine video container), so this falls back to exactly what the old default preview showed instead of
    // just an error, and only shows the error text if even that comes back empty.
    private void ShowFailure()
    {
        Player.Visibility = Visibility.Collapsed;
        AudioPlaceholder.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
        ImgFileIconFallback();

        var pathForFallback = _currentPath;
        Task.Run(() => IconService.GetThumbnail(pathForFallback, 512)).ContinueWith(t =>
        {
            if (_currentPath != pathForFallback) return; // navigated away since this started
            if (t.Status == TaskStatus.RanToCompletion && t.Result != null)
            {
                ImgErrorIcon.Source = t.Result;
                ImgErrorIcon.Width = double.NaN;
                ImgErrorIcon.Height = double.NaN;
                ImgErrorIcon.MaxHeight = 420;
                TxtError.Visibility = Visibility.Collapsed;
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ImgFileIconFallback()
    {
        ImgErrorIcon.Source = IconService.GetIcon(_currentPath, false);
        TxtError.Text = TranslationService.Get("QuickLook_MediaPlaybackFailed");
        TxtError.Visibility = Visibility.Visible;
    }

    private void UpdateProgress()
    {
        if (_isDraggingScrub || _duration <= TimeSpan.Zero) return;
        var fraction = Player.Position.TotalSeconds / _duration.TotalSeconds;
        ScrubFill.Width = Math.Clamp(fraction, 0, 1) * ScrubTrack.ActualWidth;
        TxtCurrentTime.Text = FormatTime(Player.Position);
    }

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayPause();

    private void Player_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => TogglePlayPause();

    private void TogglePlayPause()
    {
        if (_duration <= TimeSpan.Zero && !_isPlaying) return; // media never opened successfully
        _isPlaying = !_isPlaying;
        if (_isPlaying) Player.Play(); else Player.Pause();
        UpdatePlayPauseIcon();
    }

    private void UpdatePlayPauseIcon() => IconPlayPause.Data = Geometry.Parse(_isPlaying ? PauseIconData : PlayIconData);

    private void BtnMute_Click(object sender, RoutedEventArgs e)
    {
        Player.IsMuted = !Player.IsMuted;
        UpdateMuteIcon();
    }

    private void UpdateMuteIcon() => IconMute.Data = Geometry.Parse(Player.IsMuted ? MutedIconData : VolumeIconData);

    private void ScrubTrack_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_duration <= TimeSpan.Zero) return;
        _isDraggingScrub = true;
        ScrubTrack.CaptureMouse();
        SeekToMousePosition(e, forceSeek: true);
    }

    private void ScrubTrack_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingScrub) SeekToMousePosition(e, forceSeek: false);
    }

    private void ScrubTrack_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingScrub) return;
        _isDraggingScrub = false;
        ScrubTrack.ReleaseMouseCapture();
        if (_pendingScrubTarget is { } target)
        {
            Player.Position = target;
            _pendingScrubTarget = null;
        }
    }

    private void SeekToMousePosition(MouseEventArgs e, bool forceSeek)
    {
        var width = ScrubTrack.ActualWidth;
        if (width <= 0) return;
        var x = Math.Clamp(e.GetPosition(ScrubTrack).X, 0, width);
        var fraction = x / width;
        ScrubFill.Width = x;
        var target = TimeSpan.FromSeconds(fraction * _duration.TotalSeconds);
        TxtCurrentTime.Text = FormatTime(target);

        var now = DateTime.UtcNow;
        if (forceSeek || now - _lastScrubSeekTime >= ScrubSeekThrottle)
        {
            _lastScrubSeekTime = now;
            Player.Position = target;
            _pendingScrubTarget = null;
        }
        else
        {
            _pendingScrubTarget = target;
        }
    }

    private static string FormatTime(TimeSpan ts) =>
        ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");

    public void Dispose()
    {
        _positionTimer.Stop();
        try { Player.Stop(); Player.Close(); } catch { }
        Player.Source = null;
    }
}
