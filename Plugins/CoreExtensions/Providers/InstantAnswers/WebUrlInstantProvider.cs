using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

/// <summary>
/// Recognizes a web address (with an explicit http/https scheme) typed or pasted into the
/// search box and offers to open it in the default browser on Enter — for the common case of
/// a URL copied from elsewhere.
/// </summary>
public class WebUrlInstantProvider : IInstantResultProvider
{
    public string Name => TranslationService.Get("WebUrl_Name");

    // Globe icon.
    private const string GlobeIcon = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z";

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        var url = TryGetUrl(query);
        if (url == null)
            yield break;

        yield return new InstantResultItem
        {
            Title = url,
            Description = TranslationService.Get("WebUrl_OpenHint"),
            IconData = GlobeIcon,
            IconColor = "AccentBlue",
            ActionType = "Execute",
            ActionArgument = url,
            TabCompletion = url
        };
    }

    /// <summary>Returns the URL for the query when it starts with an http/https scheme, else null.</summary>
    private static string? TryGetUrl(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var q = query.Trim();
        // Copied/typed URLs have no spaces; require a minimum length to avoid noise.
        if (q.Length < 8 || q.IndexOf(' ') >= 0)
            return null;

        if (Uri.TryCreate(q, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            !string.IsNullOrEmpty(uri.Host))
        {
            return q;
        }

        return null;
    }
}
