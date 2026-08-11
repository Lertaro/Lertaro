using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lertaro.App.Views.LocalSend;

/// <summary>
/// Item view model with per-file individual progress tracking for LocalSend receive window.
/// ponytail: Split out purely to keep LocalSendReceiveWindow.xaml.cs under the repo's 300-line limit.
/// </summary>
public sealed class LocalSendReceiveFileItem : INotifyPropertyChanged
{
    private double _progressPercentage;
    private string _statusText = string.Empty;
    private bool _isFinished;
    private bool _isFailed;
    private bool _showProgress;

    public required string FileId { get; init; }
    public required string FileName { get; init; }
    public required string DisplayName { get; init; }
    public required long Size { get; init; }
    public required string SizeText { get; init; }

    public double ProgressPercentage
    {
        get => _progressPercentage;
        set { if (Math.Abs(_progressPercentage - value) > 0.01) { _progressPercentage = value; OnPropertyChanged(); } }
    }

    public string StatusText
    {
        get => _statusText;
        set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
    }

    public bool IsFinished
    {
        get => _isFinished;
        set { if (_isFinished != value) { _isFinished = value; OnPropertyChanged(); } }
    }

    public bool IsFailed
    {
        get => _isFailed;
        set { if (_isFailed != value) { _isFailed = value; OnPropertyChanged(); } }
    }

    private bool _isCanceled;

    public bool IsCanceled
    {
        get => _isCanceled;
        set { if (_isCanceled != value) { _isCanceled = value; OnPropertyChanged(); } }
    }

    public bool ShowProgress
    {
        get => _showProgress;
        set { if (_showProgress != value) { _showProgress = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
}
