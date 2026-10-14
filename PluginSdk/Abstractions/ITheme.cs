using System.Windows;

namespace Lertaro.PluginSdk.Abstractions;

public interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    ResourceDictionary GetResources();

    /// <summary>
    /// No longer applied to anything: a window with <see cref="Window.AllowsTransparency"/> set is a layered
    /// window, and a layered window cannot render ClearType, so this app gives every window an opaque
    /// surface now and reads no theme's opacity. Kept because third-party themes implement it and the
    /// resource key is still parsed; a theme may keep returning whatever it likes.
    /// </summary>
    [Obsolete("Window transparency is no longer supported; this value is ignored.")]
    double WindowOpacity => 1.0;
}
