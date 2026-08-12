using Lertaro.Core.IndexV2.Space;

namespace Lertaro.App.Views.SpaceAnalyzer;

internal readonly record struct SpaceAnalyzerLocation(string? Path, string Name);

// Split out to keep SpaceAnalyzerView under the repository's per-file line limit. Availability is
// determined from the index hierarchy, not Directory.Exists: a disabled index is unavailable here even
// when its files still exist on disk.
internal static class SpaceAnalyzerLocationResolver
{
    public static async Task TrimUnavailableAsync(
        IList<SpaceAnalyzerLocation> history,
        Func<string?, CancellationToken, Task<IReadOnlyList<SpaceIndexEntry>>> loadEntries,
        CancellationToken token)
    {
        while (history.Count > 1)
        {
            var current = history[^1].Path;
            var parentEntries = await loadEntries(history[^2].Path, token);
            if (parentEntries.Any(entry => entry.IsDirectory &&
                string.Equals(entry.Path, current, StringComparison.OrdinalIgnoreCase)))
                return;
            history.RemoveAt(history.Count - 1);
        }
    }
}
