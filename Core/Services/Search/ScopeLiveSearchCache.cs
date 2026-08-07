using System.Collections.Concurrent;

namespace Lertaro.Core.Services.Search;

// One SearchService belongs to one search window, so this keeps the expensive live-search eligibility
// probe stable while the user types in the same directory and is discarded with that window.
internal sealed class ScopeLiveSearchCache
{
    private readonly ConcurrentDictionary<string, Lazy<bool>> _decisions = new(StringComparer.OrdinalIgnoreCase);

    public bool GetOrAdd(string directory, Func<string, bool> probe) => _decisions.GetOrAdd(directory,
        path => new Lazy<bool>(() => probe(path), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
}
