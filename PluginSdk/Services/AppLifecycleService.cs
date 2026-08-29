namespace Lertaro.PluginSdk.Services;

/// <summary>Provides lifecycle requests that must be performed by the host application.</summary>
public static class AppLifecycleService
{
    /// <summary>Host callback for requesting an orderly application restart.</summary>
    public static Func<bool>? RequestRestartFunc { get; set; }

    /// <summary>Requests a restart and returns whether the host accepted the request.</summary>
    public static bool RequestRestart() => RequestRestartFunc?.Invoke() == true;
}
