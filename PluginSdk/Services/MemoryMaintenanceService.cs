namespace Lertaro.PluginSdk.Services;

/// <summary>Requests process memory maintenance from the host application.</summary>
public static class MemoryMaintenanceService
{
    /// <summary>Host callback for scheduling a best-effort working-set trim.</summary>
    public static Action? RequestTrimAction { get; set; }

    /// <summary>
    /// Requests a trim when the host considers the process idle. This does not guarantee that memory is
    /// returned to the operating system and may be ignored by hosts that do not provide the service.
    /// </summary>
    public static void RequestTrim() => RequestTrimAction?.Invoke();
}
