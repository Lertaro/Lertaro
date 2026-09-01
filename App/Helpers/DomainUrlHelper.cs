using System.Text.RegularExpressions;

namespace Lertaro.App.Helpers;

internal static class DomainUrlHelper
{
    private const string UserInfoToken = @"(?:[A-Za-z0-9._~!$&'()*+,;=:-]|%[0-9A-Fa-f]{2})";
    private const string HostLabel = @"[\p{L}\p{M}\p{N}_-]{1,63}";
    private const string Tld = @"(?:[\p{L}]{2,63}|xn--[A-Za-z0-9-]{1,59})";
    private const string DnsHost = $"(?:{HostLabel}\\.)+{Tld}";
    private const string Ipv4 = @"(?:25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(?:\.(?:25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])){3}";
    private const string Ipv6 = @"(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}|(?:[0-9A-Fa-f]{1,4}:){1,7}:|(?:[0-9A-Fa-f]{1,4}:){1,6}:[0-9A-Fa-f]{1,4}|(?:[0-9A-Fa-f]{1,4}:){1,5}(?::[0-9A-Fa-f]{1,4}){1,2}|(?:[0-9A-Fa-f]{1,4}:){1,4}(?::[0-9A-Fa-f]{1,4}){1,3}|(?:[0-9A-Fa-f]{1,4}:){1,3}(?::[0-9A-Fa-f]{1,4}){1,4}|(?:[0-9A-Fa-f]{1,4}:){1,2}(?::[0-9A-Fa-f]{1,4}){1,5}|[0-9A-Fa-f]{1,4}:(?:(?::[0-9A-Fa-f]{1,4}){1,6})|:(?:(?::[0-9A-Fa-f]{1,4}){1,7}|:)";
    private const string Host = $"(?:{DnsHost}|{Ipv4}|\\[(?:{Ipv6})\\])";
    private const string Port = @"(?:0|[1-9][0-9]{0,3}|[1-5][0-9]{4}|6[0-4][0-9]{3}|65[0-4][0-9]{2}|655[0-2][0-9]|6553[0-5])";
    private const string PathOrQueryToken = @"(?:[\p{L}\p{M}\p{N}._~!$&'()*+,;=:@/?-]|%[0-9A-Fa-f]{2})";

    // This is deliberately the narrow grammar needed for protocol-less HTTP(S) completion, not a
    // universal URL parser. It keeps the host's final label recognizable as a public-style TLD while
    // allowing URI user info, IP literals, ports, path segments, query parameters, and fragments.
    private static readonly Regex BareHttpUrlPattern = new(
        $@"\A(?:{UserInfoToken}+@)?{Host}(?::{Port})?(?:/{PathOrQueryToken}*)?(?:\?{PathOrQueryToken}*)?(?:\#{PathOrQueryToken}*)?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryBuildWebUrls(string? input, out string httpsUrl, out string httpUrl)
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
