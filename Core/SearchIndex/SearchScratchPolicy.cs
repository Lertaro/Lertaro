using System.Runtime.CompilerServices;

namespace Lertaro.Core.SearchIndex;

/// <summary>Limits retained capacity in reusable search buffers.</summary>
/// <remarks>
/// List.Clear and Dictionary.Clear retain capacity. Broad searches can therefore leave large arrays
/// reachable from static pools and thread statics. Release buffers over the byte budget after use;
/// ordinary searches continue to reuse their buffers.
///
/// The limit is per buffer and measured in bytes because entry sizes differ. Workers each retain a
/// buffer, so memory use scales with core count. In measurements, queries returning 24k–41k results
/// needed at most 2,048 entries per worker. The 128 KB limit allows 4,096 matcher entries and caps
/// retained worker storage at about 2 MB on the measured machine, down from about 28 MB.
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
