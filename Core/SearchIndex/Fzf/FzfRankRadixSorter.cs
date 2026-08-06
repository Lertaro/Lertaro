using System.Runtime.InteropServices;

namespace Lertaro.Core.SearchIndex.Fzf;

// Orders ranks by sort key, ties broken by entry index -- the same order as FzfResultRank.Compare,
// which the small-input path below still uses directly.
//
// Twelve LSD passes, over the four bytes of the entry index and then the eight of the sort key. An LSD
// radix sort is stable, so ordering by the tie-break FIRST and the key second leaves equal keys in
// entry-index order, and the tie-break costs four extra passes over the data.
//
// It used to run the eight key passes and then walk the result looking for runs of equal keys, calling
// List.Sort on each one. Equal keys are not the exception here: a sort key is computed per unique name,
// so every row sharing a name shares its key, and a set of a few thousand ranks contains hundreds of
// such runs. Each was a separate List.Sort with its own bounds checking and a non-inlinable IComparer
// call per comparison, and together they cost more than the radix sort they were correcting.
internal static class FzfRankRadixSorter
{
    // Reused across searches: the scratch is a few thousand 16-byte structs, which was two arrays
    // allocated and thrown away on every keystroke.
    [ThreadStatic]
    private static FzfRank[]? _scratch;

    public static void Sort(List<FzfRank> ranks)
    {
        var count = ranks.Count;
        if (count < 128)
        {
            ranks.Sort(FzfResultRank.Compare);
            return;
        }

        var live = CollectionsMarshal.AsSpan(ranks);
        FzfRank[] buffer;
        if (_scratch != null && _scratch.Length >= count)
        {
            buffer = _scratch;
        }
        else
        {
            buffer = new FzfRank[Math.Max(count, 4096)];
            // Kept for the next search only while it is a size a next search would plausibly want. A
            // whole-drive query sizes this to its own result count, and holding that forever is a high
            // water mark nothing ever gives back -- see SearchScratchPolicy. An existing smaller one is
            // deliberately left in place rather than replaced by the oversized one.
            if (SearchScratchPolicy.WorthRetaining<FzfRank>(buffer.Length))
                _scratch = buffer;
        }
        var scratch = buffer.AsSpan(0, count);

        var from = live;
        var to = scratch;
        var passes = 0;
        Span<int> buckets = stackalloc int[256];
        Span<int> offsets = stackalloc int[256];

        for (var pass = 0; pass < 12; pass++)
        {
            buckets.Clear();
            for (var i = 0; i < count; i++)
                buckets[ByteOf(from[i], pass)]++;

            // Every value shares this byte, so the pass would only copy the data unchanged. Skipping it
            // also keeps `passes` honest about where the data currently lives.
            if (buckets[ByteOf(from[0], pass)] == count)
                continue;

            offsets.Clear();
            for (var i = 1; i < 256; i++)
                offsets[i] = offsets[i - 1] + buckets[i - 1];

            for (var i = 0; i < count; i++)
                to[offsets[ByteOf(from[i], pass)]++] = from[i];

            // Not a tuple swap: Span is a ref struct and cannot be a generic type argument.
            var swap = from;
            from = to;
            to = swap;
            passes++;
        }

        if ((passes & 1) == 1)
            from.CopyTo(live);
    }

    // Passes 0-3 are the entry index, 4-11 the sort key. The index is signed, and flipping its top bit
    // makes the unsigned byte order agree with int comparison -- entry indices are row numbers and
    // never negative today, but a radix sort that quietly depends on that is a trap.
    private static int ByteOf(in FzfRank rank, int pass)
        => pass < 4
            ? (int)((((uint)rank.EntryIndex ^ 0x8000_0000u) >> (pass * 8)) & 0xFF)
            : (int)((rank.SortKey >> ((pass - 4) * 8)) & 0xFF);
}
