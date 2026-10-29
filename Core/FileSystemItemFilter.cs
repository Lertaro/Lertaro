namespace Lertaro.Core;

public static class FileSystemItemFilter
{
    public static bool IsHiddenOrSystem(FileAttributes attributes)
        => (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    public static bool IsHiddenOrSystem(SearchResult result)
    {
        if (result == null)
            return false;

        // Prefer memory cached attributes to avoid triggering network or physical disk IO (critical WSL fix)
        return IsHiddenOrSystem(result.Attributes);
    }

    public static bool IsHiddenOrSystem(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // File.GetAttributes has no timeout, and this is asked once per candidate on the path that paints the
        // window -- including favourites, which get the same exclusion check the index side applies. One
        // favourite on a share that answers slowly turned a three-millisecond query into a one-second one, and
        // it was asked again on every keystroke. Attributes do not change between keystrokes, so a short memo
        // is enough.
        if (AttributeCache.TryGetValue(path, out var cached))
        {
            if (Environment.TickCount64 - cached.At < AttributeCacheMs)
                return cached.Hidden;
        }

        bool hidden;
        try
        {
            hidden = IsHiddenOrSystem(File.GetAttributes(path));
        }
        catch
        {
            hidden = false;
        }

        AttributeCache[path] = (hidden, Environment.TickCount64);
        return hidden;
    }

    private const long AttributeCacheMs = 30_000;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (bool Hidden, long At)> AttributeCache = new(StringComparer.OrdinalIgnoreCase);
}
