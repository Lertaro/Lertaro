namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

/// <summary>
/// Optional capability of a preview control (the UIElement returned from
/// <see cref="IFilePreviewProvider.CreatePreview"/>): it can re-point itself at a new target in place,
/// so the host can reuse it instead of tearing it down and rebuilding on every selection change.
/// </summary>
public interface IReusablePreview
{
    /// <summary>
    /// Tries to switch this already-shown preview to <paramref name="path"/> in place.
    /// Returns true if handled (the host keeps this control as-is); false to fall back to rebuilding.
    /// </summary>
    bool TrySetTarget(string path, bool isDir);
}
