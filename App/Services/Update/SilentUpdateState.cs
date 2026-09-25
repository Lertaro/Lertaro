using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lertaro.App.Services.Update;

/// <summary>
/// What a background silent update is doing right now, for the one surface that can show it: the About page
/// opened while the download is still running.
/// </summary>
/// <remarks>
/// Silent means no dialog, not no trace. Without this the user sees an App quietly pulling tens of
/// megabytes and then exiting with no explanation of either.
///
/// One instance bound through <c>{x:Static}</c>, because the update runs on a startup background task with
/// no window of its own to own the state. WPF marshals a scalar property change onto the UI thread, so the
/// download loop reports straight from its own thread.
/// </remarks>
public sealed class SilentUpdateState : INotifyPropertyChanged
{
    public static SilentUpdateState Current { get; } = new();

    private string _text = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
                return;

            _text = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsActive));
        }
    }

    /// <summary>Empty is the resting state, and the About page's line hides itself on it.</summary>
    public bool IsActive => _text.Length > 0;

    public static void Report(string? text) => Current.Text = text ?? string.Empty;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
