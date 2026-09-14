namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

internal readonly record struct InlinePathDisplay(string Text, bool NeedsTextTrimming);

/// <summary>
/// Builds the path text used by an inline result row. The full path is preferred; only an overflowing
/// path is compacted by removing middle directories while keeping the final directory intact.
/// </summary>
internal static class InlinePathDisplayFormatter
{
    internal static InlinePathDisplay Format(string path, double availableWidth, Func<string, double> measure)
    {
        if (string.IsNullOrEmpty(path) || availableWidth <= 0 || measure(path) <= availableWidth)
            return new InlinePathDisplay(path, false);

        var parsed = Parse(path);
        if (parsed.Segments.Count == 0)
            return new InlinePathDisplay(path, true);

        var best = string.Empty;
        for (var prefixCount = 0; prefixCount < parsed.Segments.Count; prefixCount++)
        {
            var candidate = Compose(parsed, prefixCount, includeRoot: true);
            if (measure(candidate) > availableWidth)
                break;

            best = candidate;
        }

        if (string.IsNullOrEmpty(best))
        {
            var withoutRoot = Compose(parsed, 0, includeRoot: false);
            if (measure(withoutRoot) <= availableWidth)
                best = withoutRoot;
        }

        if (string.IsNullOrEmpty(best) && measure(parsed.Segments[^1]) <= availableWidth)
        {
            best = parsed.Segments[^1];
        }

        return string.IsNullOrEmpty(best)
            ? new InlinePathDisplay(path, true)
            : new InlinePathDisplay(best, false);
    }

    private static string Compose(ParsedPath path, int prefixCount, bool includeRoot)
    {
        var parts = new List<string>(prefixCount + 2);
        parts.AddRange(path.Segments.Take(prefixCount));
        parts.Add("...");
        parts.Add(path.Segments[^1]);

        var suffix = string.Join(path.Separator, parts);
        return includeRoot && !string.IsNullOrEmpty(path.Root) ? path.Root + suffix : suffix;
    }

    private static ParsedPath Parse(string path)
    {
        var rootLength = FindRootLength(path);
        var root = path[..rootLength];
        var remainder = path[rootLength..];
        var separator = path.Contains('\\') ? '\\' : '/';
        var segments = remainder
            .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (root.Length > 0 && root[^1] != '\\' && root[^1] != '/')
            root += separator;

        return new ParsedPath(root, separator, segments);
    }

    private static int FindRootLength(string path)
    {
        if (path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            var serverEnd = path.IndexOfAny(new[] { '\\', '/' }, 2);
            if (serverEnd < 0) return path.Length;
            var shareEnd = path.IndexOfAny(new[] { '\\', '/' }, serverEnd + 1);
            return shareEnd < 0 ? path.Length : shareEnd + 1;
        }

        if (path.Length >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            return 3;

        if (path.Length >= 2 && path[1] == ':' && path.Length == 2)
            return 2;

        if (path.Length > 0 && (path[0] == '\\' || path[0] == '/'))
            return 1;

        var colon = path.IndexOf(':');
        if (colon >= 0 && colon + 1 < path.Length && (path[colon + 1] == '\\' || path[colon + 1] == '/'))
            return colon + 2;

        return 0;
    }

    private readonly record struct ParsedPath(string Root, char Separator, List<string> Segments);
}
