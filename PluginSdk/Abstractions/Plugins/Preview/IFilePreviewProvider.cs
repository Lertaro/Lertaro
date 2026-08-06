using System.Windows;

namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

/// <summary>
/// Interface that allows plugins to provide custom UI previews for files or folders.
/// </summary>
public interface IFilePreviewProvider : IPluginComponent
{

    /// <summary>Gets the priority of this provider (higher runs first).</summary>
    int Priority => 0;

    /// <summary>
    /// Determines if this provider can build a preview for the given path.
    /// </summary>
    bool CanPreview(string path, bool isDir);

    /// <summary>
    /// Creates a WPF UI control displaying the preview of the path.
    /// </summary>
    UIElement CreatePreview(string path, bool isDir);

    /// <summary>
    /// When true, this provider's actual preview surface is a separate, externally-managed window --
    /// not the <see cref="UIElement"/> <see cref="CreatePreview"/> returns, which the host then never
    /// shows. The host preview panel hides itself instead of displaying that content, since there would
    /// be nothing useful inside its own chrome.
    /// </summary>
    bool RendersExternally => false;
}
