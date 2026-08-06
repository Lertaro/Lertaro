namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Where the user was last browsing: the folder an Explorer window or a file dialog was last showing.
/// </summary>
/// <remarks>
/// Filled in by the host's own window tracking, which watches every application's file dialogs, not just
/// this app's own UI -- so "last active" means the last folder the user was actually looking at anywhere,
/// which is not something a plugin could work out for itself.
///
/// The other direction from <c>IActivePathCollector</c>, which is how a plugin TELLS the host what folder
/// a third-party file manager is showing. This is how it asks.
/// </remarks>
public static class ExplorerPathService
{
    /// <summary>Set by the host at startup.</summary>
    public static Func<string?>? GetLastActivePathFunc { get; set; }

    /// <summary>
    /// The last folder browsed to, or null when nothing has been yet. Not guaranteed to still exist:
    /// it is a record of where the user went, and the folder can have been deleted or unplugged since.
    /// </summary>
    public static string? GetLastActivePath() => GetLastActivePathFunc?.Invoke();
}
