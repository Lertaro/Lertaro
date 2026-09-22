namespace Lertaro.Plugins.BrowserData;

/// <summary>
/// The browser profiles the plugin ships with. One list on purpose: the Settings page renders a field
/// that was never touched from the schema's <c>DefaultValue</c>, while the plugin's own runtime read only
/// ever sees what has actually been stored, so the two need the same source or a fresh install shows two
/// pre-filled rows that index nothing at all.
/// </summary>
/// <remarks>
/// Each path is the folder that *holds* profiles rather than one profile, because that is the only
/// spelling that is guessable: Chromium's is fixed ("User Data"), Firefox's is the <c>Profiles</c> folder
/// whose children are randomized per install (<c>ydcqbg2n.default-release</c>) and so cannot be written
/// out in advance. <see cref="Readers.BrowserProfileDirectories.Discover"/> expands one to the profiles
/// inside it, and a path that is itself a profile keeps working unchanged.
/// </remarks>
internal static class BrowserDataDefaults
{
    public static List<BrowserProfileConfig> Profiles() =>
    [
        new() { Name = "Edge", Icon = string.Empty, Path = @"%LOCALAPPDATA%\Microsoft\Edge\User Data" },
        new() { Name = "Chrome", Icon = string.Empty, Path = @"%LOCALAPPDATA%\Google\Chrome\User Data" },
        new() { Name = "Firefox", Icon = string.Empty, Path = @"%APPDATA%\Mozilla\Firefox\Profiles" },
    ];

    /// <summary>
    /// The same rows in the shape an Array field's <c>DefaultValue</c> carries: one dictionary per row,
    /// keyed by the sub-field keys. Generated rather than written twice so the defaults the user reads in
    /// Settings are literally the defaults the plugin runs on.
    /// </summary>
    public static List<object> ProfileSchemaDefaults() =>
        Profiles().Select(p => (object)new Dictionary<string, object>
        {
            { nameof(BrowserProfileConfig.Name), p.Name },
            { nameof(BrowserProfileConfig.Icon), p.Icon },
            { nameof(BrowserProfileConfig.Path), p.Path },
        }).ToList();
}
