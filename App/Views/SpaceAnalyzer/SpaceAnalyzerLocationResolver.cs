using Lertaro.Core.IndexV2.Space;

namespace Lertaro.App.Views.SpaceAnalyzer;

internal readonly record struct SpaceAnalyzerLocation(string? Path, string Name);

// Resolves locations through the index hierarchy. A disabled index is unavailable even if its files still
// exist on disk.
internal static class SpaceAnalyzerLocationResolver
{
    public static async Task<bool> TrimUnavailableAsync(
        IList<SpaceAnalyzerLocation> history,
        Func<string?, CancellationToken, Task<IReadOnlyList<SpaceIndexEntry>>> loadEntries,
        CancellationToken token)
    {
        var changed = false;
        while (history.Count > 1)
        {
            var current = history[^1].Path;
            var parentEntries = await loadEntries(history[^2].Path, token);
            if (parentEntries.Any(entry => entry.IsDirectory &&
                string.Equals(entry.Path, current, StringComparison.OrdinalIgnoreCase)))
                return changed;
            history.RemoveAt(history.Count - 1);
            changed = true;
        }
        return changed;
    }
}
