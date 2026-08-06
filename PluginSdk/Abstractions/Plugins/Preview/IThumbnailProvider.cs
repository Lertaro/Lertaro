using System.Windows.Media;

namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

/// <summary>
/// Provides custom file thumbnails or icon overrides for search results.
/// </summary>
public interface IThumbnailProvider : IPluginComponent
{

    /// <summary>Gets the priority of this provider (higher runs first).</summary>
    int Priority => 0;

    /// <summary>
    /// Checks if this provider can generate a thumbnail/icon for the specified file path and directory state.
    /// </summary>
    bool CanProvideThumbnail(string path, bool isDir);

    /// <summary>
    /// Generates or retrieves the thumbnail for the specified file path.
    /// </summary>
    ImageSource? GetThumbnail(string path, int size);
}
