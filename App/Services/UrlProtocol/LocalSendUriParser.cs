using System.IO;
namespace Lertaro.App.Services.UrlProtocol;

internal enum LocalSendUriRequestKind
{
    Open,
    Items,
    Text
}

internal sealed record LocalSendUriRequest(
    LocalSendUriRequestKind Kind,
    IReadOnlyList<string>? Files = null,
    string? Text = null);

internal static class LocalSendUriParser
{
    internal const int MaxUriLength = 32767;
    internal const int MaxItemCount = 100;
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

        if (uri.Query.Length != 0) return false;

        var segments = uri.AbsolutePath.TrimStart('/').Split('/').ToList();
        if (segments.Count > 1 && segments[^1].Length == 0) segments.RemoveAt(segments.Count - 1);
        if (segments.Any(segment => segment.Length == 0))
        {
            if (segments.Count != 1) return false;
            segments.Clear();
        }

        if (segments.Count == 0)
        {
            request = new LocalSendUriRequest(LocalSendUriRequestKind.Open);
            return true;
        }

        var subRoute = segments[0];
        var encodedValues = segments.Skip(1).ToArray();
        if (subRoute.Equals("items", StringComparison.OrdinalIgnoreCase))
            return TryCreateItemsRequest(encodedValues, out request);

        if (subRoute.Equals("text", StringComparison.OrdinalIgnoreCase))
            return TryCreateTextRequest(encodedValues, out request);

        return false;
    }

    private static bool TryCreateItemsRequest(
        IReadOnlyList<string> encodedValues,
        out LocalSendUriRequest? request)
    {
        request = null;
        if (encodedValues.Count > MaxItemCount) return false;

        var paths = new List<string>(encodedValues.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var encodedValue in encodedValues)
        {
            if (!TryDecodeSegment(encodedValue, out var path)) return false;
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

        request = new LocalSendUriRequest(LocalSendUriRequestKind.Items, paths);
        return true;
    }

    private static bool TryCreateTextRequest(
        IReadOnlyList<string> encodedValues,
        out LocalSendUriRequest? request)
    {
        request = null;
        if (encodedValues.Count == 0)
        {
            request = new LocalSendUriRequest(LocalSendUriRequestKind.Text);
            return true;
        }

        if (encodedValues.Count != 1 || !TryDecodeSegment(encodedValues[0], out var text)
            || text.Length > MaxTextLength || text.IndexOf('\0') >= 0) return false;

        request = new LocalSendUriRequest(LocalSendUriRequestKind.Text, Text: text);
        return true;
    }

    private static bool TryDecodeSegment(string encodedValue, out string value)
    {
        value = string.Empty;
        if (encodedValue.Length == 0 || !HasValidEscapes(encodedValue)) return false;
        value = Uri.UnescapeDataString(encodedValue);
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
