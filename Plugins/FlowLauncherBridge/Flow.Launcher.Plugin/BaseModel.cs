using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Flow.Launcher.Plugin;

/// <summary>
/// Base model providing INotifyPropertyChanged implementation.
/// </summary>
public abstract class BaseModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
