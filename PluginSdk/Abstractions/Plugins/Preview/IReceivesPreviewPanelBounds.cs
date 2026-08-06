namespace Lertaro.PluginSdk.Abstractions.Plugins.Preview;

/// <summary>
/// Optional capability for a provider whose <see cref="IFilePreviewProvider.RendersExternally"/> is
/// true: receives the physical-pixel screen rectangle the host's own preview panel would have occupied
/// for the current owner window, so an externally-managed preview window can be positioned there instead
/// of wherever it would otherwise appear. Called again on every navigation to a new file this provider
/// wins (not just once), since that rectangle depends on the owner window's current position/monitor.
/// </summary>
public interface IReceivesPreviewPanelBounds
{
    void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);
}
