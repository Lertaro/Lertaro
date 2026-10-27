using System.Collections.Concurrent;
using System.IO;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.App.Helpers;

/// <summary>
/// Resolves a shortcut to the entry it points at, once per shortcut path for the life of the process.
/// </summary>
/// <remarks>
/// Resolving walks IShellLink and IPersistFile, and the one caller that needs it here -- the result
/// categorizer -- runs per row and per paint. <see cref="StartMenuShortcutResolver.ResolveShortcutTarget"/>
/// stays the uncached answer for the paths that resolve a shortcut in order to act on it (the app indexer,
/// the recent-files rows): those want today's target, and a stale answer here is only ever a classification.
///
/// The target's directory kind is cached with the target for the same reason. Asking the filesystem again
/// would put a stat on the per-row path -- and on a shortcut aimed at a share that stopped answering, that
/// stat is the SMB timeout, paid over on every paint.
/// </remarks>
public static class ShortcutTargetResolver
{
    /// <summary>A shortcut's target and the filesystem kind it points at.</summary>
    public sealed record ShortcutTarget(string Path, bool IsDirectory);

    private static readonly ConcurrentDictionary<string, ShortcutTarget?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The shortcut's target and its filesystem kind, or null when the path is not a shortcut or has no
    /// readable target.
    /// </summary>
    public static ShortcutTarget? Resolve(string path)
    {
        if (!path.EndsWith(SearchCategoryClassifier.ShortcutExtension, StringComparison.OrdinalIgnoreCase))
            return null;

        return Cache.GetOrAdd(path, static shortcut =>
        {
            var target = StartMenuShortcutResolver.ResolveShortcutTarget(shortcut);
            return string.IsNullOrEmpty(target)
                ? null
                : new ShortcutTarget(target, Directory.Exists(target));
        });
    }
}
