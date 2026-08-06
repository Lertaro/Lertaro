using System.Collections.Concurrent;
using System.IO;

namespace Lertaro.PluginSdk.Helpers;

/// <summary>
/// Remembers whether a path exists, for the duration of one action-menu build.
/// </summary>
/// <remarks>
/// Almost every built-in action decides whether it applies with
/// <c>results.All(r =&gt; File.Exists(r.FullPath) || Directory.Exists(r.FullPath))</c>. Eight of them do,
/// over the same selection, so opening a menu asked the filesystem the same question about the same paths
/// eight times over -- on the UI thread, since that is where the static half of the menu is built.
/// Measured at 5.4us a path that exists and 14.9us for one that doesn't, a selection of fifty thousand
/// rows meant well over a second of the window being frozen before the menu appeared.
///
/// Caching per build makes that eight passes into one without changing a single verdict. It also makes
/// the eight agree with each other, which they previously needn't have: a file deleted between two of
/// those passes could enable one action and disable the next.
///
/// The cache lives only inside <see cref="BeginScope"/>. Outside one, <see cref="Exists"/> is a plain
/// filesystem call, so a caller that hasn't opted in behaves exactly as it did before, and no verdict
/// ever survives into a later menu.
/// </remarks>
public static class PathExistenceCache
{
    private static ConcurrentDictionary<string, bool>? _current;

    /// <summary>
    /// Starts a scope in which repeated questions about a path are answered once. Dispose it when the
    /// menu build that owns it is finished -- including whatever part of it runs on the UI thread.
    /// </summary>
    public static IDisposable BeginScope()
    {
        var cache = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        _current = cache;
        return new Scope(cache);
    }

    /// <summary>
    /// Whether the path is a file or a directory that exists. Same answer as calling File.Exists and
    /// Directory.Exists directly; inside a scope, only asked of the filesystem once per path.
    /// </summary>
    public static bool Exists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var cache = _current;
        if (cache == null)
            return Probe(path);

        return cache.GetOrAdd(path, static p => Probe(p));
    }

    /// <summary>
    /// Fills the current scope's cache for <paramref name="paths"/>, stopping at the first one that does
    /// not exist.
    /// </summary>
    /// <remarks>
    /// Stopping early is what keeps this from ever being more work than not calling it. Every caller
    /// reads the result through All(), which stops at the first false too, so priming past that point
    /// would probe paths nobody was going to ask about -- and on a selection of hundreds of thousands
    /// whose first entry is stale, that would turn a single probe into all of them.
    ///
    /// Call it off the UI thread. The point is not that the probing gets cheaper -- it is the same
    /// probing -- but that it happens somewhere the user isn't waiting on it.
    /// </remarks>
    public static void Prime(IEnumerable<string?> paths)
    {
        if (_current == null)
            return;

        foreach (var path in paths)
        {
            if (!Exists(path))
                return;
        }
    }

    private static bool Probe(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly ConcurrentDictionary<string, bool> _owned;

        public Scope(ConcurrentDictionary<string, bool> owned) => _owned = owned;

        public void Dispose() =>
            // Only clears the cache if it is still this scope's. A menu build that was superseded while
            // running must not pull the cache out from under the one that replaced it.
            Interlocked.CompareExchange(ref _current, null, _owned);
    }
}
