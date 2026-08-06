using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lertaro.App.ViewModels.LocalSend;

/// <summary>One file's sender-side transfer state, which becomes complete only after a successful HTTP response.</summary>
public sealed class LocalSendSendFileItem : INotifyPropertyChanged
{
    private double _progressPercentage;
    private string _statusText = string.Empty;
    private bool _isConfirmed;
    private bool _showProgress;

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

    public bool IsConfirmed
    {
        get => _isConfirmed;
        set { if (_isConfirmed != value) { _isConfirmed = value; OnPropertyChanged(); } }
    }

    public bool ShowProgress
    {
        get => _showProgress;
        set { if (_showProgress != value) { _showProgress = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
