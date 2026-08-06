using System.Runtime.CompilerServices;

namespace Lertaro.Core.SearchIndex;

/// <summary>
/// How large a reusable search buffer is allowed to get before it stops being worth keeping.
/// </summary>
/// <remarks>
/// The search path pools and reuses its working buffers because reallocating them on every keystroke was
/// measurable. What none of them did was ever give anything back: each grew to fit the biggest search it
/// had seen and stayed there, because Clear on a List or a Dictionary resets the count and not the
/// capacity. That was invisible while the full window asked for a thousand results; now that it asks for
/// every match on the drive, one search for a single letter sizes all of them for its own result count
/// and the service holds that for the rest of its life. Nor can a collection help, since these are
/// reachable from static pools and thread statics and so are not garbage.
///
/// So a buffer that grew past the budget below is released instead of retained, and only a search big
/// enough to have caused the problem pays for one reallocation next time.
///
/// The budget is in BYTES rather than entries, and is per buffer. Every one of these lives in a thread
/// static or a per-worker pool, so whatever is allowed here is paid once per core: on a 16-core machine
/// the matcher alone keeps seventeen of them. An entry count cannot express that, because the same count
/// means very different amounts for a 16-byte rank and a 32-byte match -- which is exactly how the first
/// attempt at this went wrong. Sixty-four thousand entries sounded modest and was 2MB per worker, so a
/// single broad query still ended with 18MB parked across the pool, all of it at precisely the retention
/// ceiling.
///
/// Measured to pick the number: an ordinary query returning 24k-41k results ends with its busiest worker
/// holding 2,048 entries, and a whole-drive query pins every worker at the ceiling whatever the ceiling
/// is. 128KB leaves ordinary use entirely untouched with room to spare -- 4,096 entries for the matcher's
/// hit list, twice the largest ever observed -- while capping what a broad search can leave behind at
/// about 2MB across all workers rather than 28MB.
/// </remarks>
internal static class SearchScratchPolicy
{
    /// <summary>
    /// Bytes above which a reused buffer is dropped rather than kept. Per buffer, and there is one per
    /// core -- see the remarks above before raising it.
    /// </summary>
    public const int MaxRetainedBytes = 128 * 1024;

    /// <summary>Whether a buffer of this many <typeparamref name="T"/> is small enough to be worth holding on to.</summary>
    public static bool WorthRetaining<T>(int entries) => (long)entries * Unsafe.SizeOf<T>() <= MaxRetainedBytes;

    /// <summary>
    /// What a dictionary spends per entry: the key and value, the hash code and next-index pair stored
    /// alongside them, and its share of the bucket array. An approximation, which is all a threshold
    /// needs -- it only has to separate "an ordinary query's buffer" from "a whole drive's".
    /// </summary>
    private static int DictionaryEntryBytes<TKey, TValue>() => Unsafe.SizeOf<TKey>() + Unsafe.SizeOf<TValue>() + 12;

    /// <summary>
    /// Empties a list, releasing its backing array outright when it had grown past the budget.
    /// TrimExcess after Clear frees the array rather than merely forgetting the contents.
    /// </summary>
    public static void ClearAndTrim<T>(List<T> list)
    {
        list.Clear();
        if (!WorthRetaining<T>(list.Capacity))
            list.TrimExcess();
    }

    /// <summary>Same, for a dictionary -- its buckets survive Clear exactly as a list's array does.</summary>
    public static void ClearAndTrim<TKey, TValue>(Dictionary<TKey, TValue> map) where TKey : notnull
    {
        // Judged before the Clear: Count is zero afterwards, so deciding then would find every dictionary
        // small and trim none of them.
        var wasOversized = (long)map.Count * DictionaryEntryBytes<TKey, TValue>() > MaxRetainedBytes;
        map.Clear();
        if (wasOversized)
            map.TrimExcess();
    }
}
