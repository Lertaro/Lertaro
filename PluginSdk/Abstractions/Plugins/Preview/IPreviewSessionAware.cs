namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

/// <summary>
/// Optional capability of a preview provider that caches native resources across previews (e.g. a pool of
/// out-of-process preview handlers kept alive so navigating between files doesn't re-spawn them). The host
/// calls <see cref="EndPreviewSession"/> when the preview session ends — the owning window closes — so
/// those resources are released together.
/// </summary>
public interface IPreviewSessionAware
{
    /// <summary>Releases any resources cached across previews for the just-ended session.</summary>
    void EndPreviewSession();
}
