namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Determines whether the shared Flow host is needed by any Lertaro component.
/// </summary>
internal static class FlowLauncherBridgeEnablement
{
    internal static bool IsRuntimeEnabled(
        Func<string, string, string, bool> isComponentEnabled,
        string dllName)
        => isComponentEnabled(dllName, "InstantProvider", "FlowInstantResultProvider")
            || isComponentEnabled(dllName, "FilePreviewProvider", "FlowPreviewProvider");
}
