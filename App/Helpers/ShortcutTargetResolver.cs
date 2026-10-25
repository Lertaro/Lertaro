using System.Collections.Concurrent;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.App.Helpers;

/// <summary>
/// Resolves a .lnk to the file it points at, once per path for the life of the process.
/// </summary>
/// <remarks>
/// Resolving walks IShellLink and IPersistFile, and it happens on paths that run per row and per paint:
/// the shortcut de-duplication and the category classifier both need the target. One entry per shortcut is
/// enough because the target of a .lnk does not change under it in practice, and a stale answer costs only
/// a classification, never a wrong row.
/// </remarks>
public static class ShortcutTargetResolver
{
    private static readonly ConcurrentDictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The target of a shortcut path, or null when it is not a shortcut or cannot be resolved.</summary>
    public static string? Resolve(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return null;

        if (Cache.TryGetValue(path, out var cached))
            return cached;

        var target = StartMenuShortcutResolver.ResolveShortcutTarget(path);
        if (string.IsNullOrEmpty(target))
            return null;

        Cache[path] = target;
        return target;
    }
}
