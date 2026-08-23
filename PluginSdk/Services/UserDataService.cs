namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Exposes the host application's resolved per-user data directory without coupling plugins to App or Core.
/// </summary>
public static class UserDataService
{
    /// <summary>Set by the host application during startup.</summary>
    public static Func<string?>? GetUserDataDirectoryFunc { get; set; }

    /// <summary>Gets the resolved per-user data directory, or null when the host has not wired it.</summary>
    public static string? GetUserDataDirectory() => GetUserDataDirectoryFunc?.Invoke();
}
