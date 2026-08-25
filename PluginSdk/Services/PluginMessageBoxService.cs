using System.Windows;

namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Provides a host-owned message box without coupling plugins to a specific host UI assembly.
/// </summary>
public static class PluginMessageBoxService
{
    /// <summary>
    /// Delegate assigned by the host application. It receives the same arguments as the plugin API.
    /// </summary>
    public static Func<string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult, MessageBoxResult>? ShowFunc { get; set; }

    /// <summary>
    /// Shows a message box through the host when available, otherwise using the platform fallback.
    /// </summary>
    public static MessageBoxResult Show(
        string messageBoxText,
        string caption = "",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.OK) =>
        ShowFunc?.Invoke(messageBoxText, caption, button, icon, defaultResult)
        ?? MessageBox.Show(messageBoxText, caption, button, icon, defaultResult);
}
