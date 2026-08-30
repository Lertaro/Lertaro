namespace Lertaro.App.Helpers;

internal static class DomainUrlHelper
{
    public static bool TryBuildHttpsUrl(string? input, out string url)
    {
        url = string.Empty;
        var candidate = input?.Trim();
        if (string.IsNullOrEmpty(candidate) || !IsBareDomain(candidate))
            return false;

        url = $"https://{candidate}";
        return true;
    }

    private static bool IsBareDomain(string candidate)
    {
        if (candidate.Contains("://", StringComparison.Ordinal) || candidate.Contains('\\'))
            return false;

        if (candidate.Any(char.IsWhiteSpace))
            return false;

        if (!Uri.TryCreate($"https://{candidate}", UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || Uri.CheckHostName(uri.Host) != UriHostNameType.Dns
            || !uri.Host.Contains('.')
            || uri.Host.StartsWith('.')
            || uri.Host.EndsWith('.')
            || uri.Host.Contains(".."))
            return false;

        return true;
    }
}
