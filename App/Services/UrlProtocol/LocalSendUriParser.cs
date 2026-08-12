using System.IO;
using System.Net;

namespace Lertaro.App.Services.UrlProtocol;

internal enum LocalSendUriRequestKind
{
    Open,
    Files,
    Text
}

internal sealed record LocalSendUriRequest(
    LocalSendUriRequestKind Kind,
    IReadOnlyList<string>? Files = null,
    string? Text = null);

internal static class LocalSendUriParser
{
    internal const int MaxUriLength = 32767;
    internal const int MaxPathCount = 100;
    internal const int MaxTextLength = 16384;

    public static bool TryParse(Uri uri, out LocalSendUriRequest? request)
    {
        request = null;
        if (!uri.Scheme.Equals("lertaro", StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("localsend", StringComparison.OrdinalIgnoreCase)
            || uri.Fragment.Length != 0)
        {
            return false;
        }

        var subRoute = uri.AbsolutePath.Trim('/');
        if (subRoute.Length == 0)
        {
            if (uri.Query.Length != 0) return false;
            request = new LocalSendUriRequest(LocalSendUriRequestKind.Open);
            return true;
        }

        if (!TryParseQuery(uri.Query, out var parameters)) return false;

        if (subRoute.Equals("items", StringComparison.OrdinalIgnoreCase))
            return TryCreateFilesRequest(parameters, out request);

        if (subRoute.Equals("text", StringComparison.OrdinalIgnoreCase))
            return TryCreateTextRequest(parameters, out request);

        return false;
    }

    private static bool TryCreateFilesRequest(
        IReadOnlyList<KeyValuePair<string, string>> parameters,
        out LocalSendUriRequest? request)
    {
        request = null;
        if (parameters.Count is 0 or > MaxPathCount
            || parameters.Any(p => !p.Key.Equals("path", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var paths = new List<string>(parameters.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in parameters)
        {
            var path = parameter.Value;
            if (path.Length == 0 || path.IndexOf('\0') >= 0 || !Path.IsPathFullyQualified(path)) return false;

            try
            {
                path = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }

            if (!File.Exists(path) && !Directory.Exists(path)) return false;
            if (seen.Add(path)) paths.Add(path);
        }

        if (paths.Count == 0) return false;
        request = new LocalSendUriRequest(LocalSendUriRequestKind.Files, paths);
        return true;
    }

    private static bool TryCreateTextRequest(
        IReadOnlyList<KeyValuePair<string, string>> parameters,
        out LocalSendUriRequest? request)
    {
        request = null;
        if (parameters.Count != 1
            || !parameters[0].Key.Equals("value", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(parameters[0].Value)
            || parameters[0].Value.Length > MaxTextLength)
        {
            return false;
        }

        request = new LocalSendUriRequest(LocalSendUriRequestKind.Text, Text: parameters[0].Value);
        return true;
    }

    private static bool TryParseQuery(string query, out List<KeyValuePair<string, string>> parameters)
    {
        parameters = new List<KeyValuePair<string, string>>();
        if (query.Length <= 1) return false;

        foreach (var pair in query.AsSpan(1).ToString().Split('&'))
        {
            if (pair.Length == 0) return false;
            var separator = pair.IndexOf('=');
            if (separator <= 0) return false;

            var encodedKey = pair[..separator];
            var encodedValue = pair[(separator + 1)..];
            if (!HasValidEscapes(encodedKey) || !HasValidEscapes(encodedValue)) return false;

            parameters.Add(new KeyValuePair<string, string>(
                WebUtility.UrlDecode(encodedKey),
                WebUtility.UrlDecode(encodedValue)));
        }

        return true;
    }

    private static bool HasValidEscapes(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '%') continue;
            if (i + 2 >= value.Length || !Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2])) return false;
            i += 2;
        }

        return true;
    }
}
