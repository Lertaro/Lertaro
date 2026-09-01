using System.Text.RegularExpressions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

/// <summary>
/// Recognizes explicit web addresses and protocol-less domains typed or pasted into the search box,
/// offering browser actions for the detected URL variants.
/// </summary>
public class WebUrlInstantProvider : IInstantResultProvider
{
    public string Name => TranslationService.Get("WebUrl_Name");

    private const string UserInfoToken = @"(?:[A-Za-z0-9._~!$&'()*+,;=:-]|%[0-9A-Fa-f]{2})";
    private const string HostLabel = @"[\p{L}\p{M}\p{N}_-]{1,63}";
    private const string Tld = @"(?:[\p{L}]{2,63}|xn--[A-Za-z0-9-]{1,59})";
    private const string DnsHost = $"(?:{HostLabel}\\.)+{Tld}";
    private const string Ipv4 = @"(?:25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(?:\.(?:25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])){3}";
    private const string Ipv6 = @"(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}|(?:[0-9A-Fa-f]{1,4}:){1,7}:|(?:[0-9A-Fa-f]{1,4}:){1,6}:[0-9A-Fa-f]{1,4}|(?:[0-9A-Fa-f]{1,4}:){1,5}(?::[0-9A-Fa-f]{1,4}){1,2}|(?:[0-9A-Fa-f]{1,4}:){1,4}(?::[0-9A-Fa-f]{1,4}){1,3}|(?:[0-9A-Fa-f]{1,4}:){1,3}(?::[0-9A-Fa-f]{1,4}){1,4}|(?:[0-9A-Fa-f]{1,4}:){1,2}(?::[0-9A-Fa-f]{1,4}){1,5}|[0-9A-Fa-f]{1,4}:(?:(?::[0-9A-Fa-f]{1,4}){1,6})|:(?:(?::[0-9A-Fa-f]{1,4}){1,7}|:)";
    private const string Host = $"(?:{DnsHost}|{Ipv4}|\\[(?:{Ipv6})\\])";
    private const string Port = @"(?:0|[1-9][0-9]{0,3}|[1-5][0-9]{4}|6[0-4][0-9]{3}|65[0-4][0-9]{2}|655[0-2][0-9]|6553[0-5])";
    private const string PathOrQueryToken = @"(?:[\p{L}\p{M}\p{N}._~!$&'()*+,;=:@/?-]|%[0-9A-Fa-f]{2})";

    private static readonly Regex BareHttpUrlPattern = new(
        $@"\A(?:{UserInfoToken}+@)?{Host}(?::{Port})?(?:/{PathOrQueryToken}*)?(?:\?{PathOrQueryToken}*)?(?:\#{PathOrQueryToken}*)?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Globe icon.
    private const string GlobeIcon = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z";

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        var normalizedQuery = query.Trim();
        var explicitUrl = TryGetExplicitUrl(normalizedQuery);
        if (explicitUrl != null)
        {
            yield return CreateResult(explicitUrl);
            yield break;
        }

        if (!TryBuildWebUrls(normalizedQuery, out var httpsUrl, out var httpUrl))
            yield break;

        yield return CreateResult(httpsUrl);
        yield return CreateResult(httpUrl);
    }

    private static InstantResultItem CreateResult(string url) => new()
    {
        Title = url,
        Description = TranslationService.Get("WebUrl_OpenHint"),
        IconData = GlobeIcon,
        IconColor = "AccentBlue",
        ActionType = "Execute",
        ActionArgument = url,
        TabCompletion = url
    };

    /// <summary>Returns the URL for an explicit http/https query, else null.</summary>
    private static string? TryGetExplicitUrl(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 8 || query.IndexOf(' ') >= 0)
            return null;

        if (Uri.TryCreate(query, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            !string.IsNullOrEmpty(uri.Host))
        {
            return query;
        }

        return null;
    }

    internal static bool TryBuildWebUrls(string? input, out string httpsUrl, out string httpUrl)
    {
        httpsUrl = string.Empty;
        httpUrl = string.Empty;
        var candidate = input?.Trim();
        if (string.IsNullOrEmpty(candidate) || !BareHttpUrlPattern.IsMatch(candidate))
            return false;

        httpsUrl = $"https://{candidate}";
        httpUrl = $"http://{candidate}";
        return true;
    }
}
