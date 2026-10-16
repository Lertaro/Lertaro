using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.PluginSdk.Abstractions.Plugins;

using Lertaro.Core.SearchIndex;
namespace Lertaro.Core.IndexV2.Alias;

// Byte-native twin of AliasGeneration for the snapshot bulk path: aliases arrive as UTF-8 segments
// via IAliasProvider.GetAliasesUtf8 (providers without a byte-native override fall back to their
// string API through the interface's default implementation, which also lowercases -- so the
// ToLowerInvariant step AliasGeneration does per alias string is already covered by contract).
// Same GetAllProviders/CanHandle/per-provider-try semantics as AliasGeneration.Generate; results
// are materialized into plain arrays so a parallel generation pass can hand them across threads
// to the strictly-serial snapshot blob writer.
//
// Providers that implement IAliasGridProvider have their reading-derived aliases stored as a per-position
// reading grid, which is the whole of that provider's reading space rather than an enumeration of it --
// see AliasGeneration.TryEncodeGrid for why that is unconditional now, and what the size difference is.
// Aliases that are not spellings of the readings (the pinyin provider's Simplified form) are emitted
// through GetStandaloneAliasesUtf8 and stay strings.
internal static class AliasGenerationUtf8
{
    internal readonly struct Result
    {
        public readonly byte[] Bytes;
        public readonly int[] SegmentLengths;
        public readonly byte[] ProviderIds;
        public readonly byte[] Kinds;

        public Result(byte[] bytes, int[] segmentLengths, byte[] providerIds, byte[] kinds)
        {
            Bytes = bytes;
            SegmentLengths = segmentLengths;
            ProviderIds = providerIds;
            Kinds = kinds;
        }
    }

    [ThreadStatic] private static AliasByteSink? _sink;

    /// <summary>
    /// Adds the reading vocabulary of one name to the snapshot's table (see AliasReadingTable): the
    /// readings a grid will reference by id, collected before any payload is encoded. Reads the reading
    /// sets only -- no enumeration, no alias strings -- so it costs a fraction of the pass that follows.
    /// </summary>
    public static void CollectReadings(string name, AliasReadingTable.Builder table)
    {
        if (string.IsNullOrEmpty(name) || AliasProviderRegistry.HasInvalidUtf16(name) || !AliasProviderRegistry.HasNonAscii(name))
            return;

        foreach (var provider in AliasProviderRegistry.GetAllProviders())
        {
            try
            {
                if (provider is not IAliasGridProvider gridProvider || !gridProvider.CanHandle(name))
                    continue;

                var sets = gridProvider.GetReadingSets(name);
                var length = Math.Min(name.Length, sets.Length);
                for (var i = 0; i < length; i++)
                {
                    var readings = sets[i];
                    if (readings == null)
                        continue;
                    foreach (var reading in readings)
                    {
                        if (!string.IsNullOrEmpty(reading))
                            table.Add(reading);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[IndexV2] Alias provider failed collecting readings for '{name}': {ex.Message}", LogLevel.Error);
            }
        }
    }

    // Generates aliases for one name, OR-ing each alias's char mask into `mask`. Returns null when
    // the name yields no aliases (the common pure-ASCII case exits on the vectorized gate).
    // `table` is the snapshot's reading vocabulary; a grid payload references it by id.
    public static Result? Generate(string name, ref ulong mask, AliasReadingTable table)
    {
        if (string.IsNullOrEmpty(name) || AliasProviderRegistry.HasInvalidUtf16(name) || !AliasProviderRegistry.HasNonAscii(name))
            return null;

        var sink = _sink ??= new AliasByteSink();
        sink.Reset();

        List<byte>? providerIds = null;
        List<byte>? kinds = null;
        foreach (var provider in AliasProviderRegistry.GetAllProviders())
        {
            try
            {
                if (!provider.CanHandle(name))
                    continue;
                var providerId = AliasProviderRegistry.GetProviderId(provider);
                AppendProvider(name, provider, providerId, table, sink, providerIds ??= new List<byte>(), kinds ??= new List<byte>(), ref mask);
            }
            catch (Exception ex)
            {
                Logger.Log($"[IndexV2] Alias provider failed for '{name}': {ex.Message}", LogLevel.Error);
            }
        }

        if (providerIds == null || providerIds.Count == 0 || sink.SegmentCount == 0)
            return null;

        var segmentLengths = new int[sink.SegmentCount];
        var total = 0;
        for (var s = 0; s < sink.SegmentCount; s++)
        {
            segmentLengths[s] = sink.Segment(s).Length;
            total += segmentLengths[s];
        }

        var bytes = new byte[total];
        var offset = 0;
        for (var s = 0; s < sink.SegmentCount; s++)
        {
            var segment = sink.Segment(s);
            segment.CopyTo(bytes.AsSpan(offset));
            offset += segment.Length;
        }

        return new Result(bytes, segmentLengths, providerIds.ToArray(), kinds!.ToArray());
    }

    // Appends one provider's chosen output: the reading grid for a provider that can express its readings,
    // plus the standalone aliases it wrote.
    private static void AppendProvider(
        string name,
        IAliasProvider provider,
        byte providerId,
        AliasReadingTable table,
        AliasByteSink dest,
        List<byte> providerIds,
        List<byte> kinds,
        ref ulong mask)
    {
        if (provider is not IAliasGridProvider gridProvider)
        {
            var before = dest.SegmentCount;
            provider.GetAliasesUtf8(name, dest);
            RecordSegments(dest, before, providerId, AliasEntryKind.String, providerIds, kinds, ref mask);
            return;
        }

        // The readings go in as their grid, always -- see AliasGeneration.TryEncodeGrid.
        if (AliasGeneration.TryEncodeGrid(name, gridProvider, table, dest, out _))
        {
            providerIds.Add(providerId);
            kinds.Add((byte)AliasEntryKind.Grid);
            mask |= GridCharMask(name, gridProvider);
        }
        else
        {
            // The provider could not express its readings as a grid (a contract violation, reported by
            // TryEncodeGrid). Its alias strings are the only correct representation of the same space, so
            // all of them go out here -- standalone ones included, and no separate step below.
            var before = dest.SegmentCount;
            gridProvider.GetAliasesUtf8(name, dest);
            RecordSegments(dest, before, providerId, AliasEntryKind.String, providerIds, kinds, ref mask);
            return;
        }

        var standaloneStart = dest.SegmentCount;
        gridProvider.GetStandaloneAliasesUtf8(name, dest);
        RecordSegments(dest, standaloneStart, providerId, AliasEntryKind.String, providerIds, kinds, ref mask);
    }

    private static void RecordSegments(
        AliasByteSink dest,
        int from,
        byte providerId,
        AliasEntryKind kind,
        List<byte> providerIds,
        List<byte> kinds,
        ref ulong mask)
    {
        for (var s = from; s < dest.SegmentCount; s++)
        {
            providerIds.Add(providerId);
            kinds.Add((byte)kind);
            mask |= FzfAlgorithm.GetCharMaskUtf8(dest.Segment(s));
        }
    }

    // The union of every character a grid can emit, which is what the query pre-gate needs: the grid
    // payload itself holds UTF-16 code units, so its raw bytes say nothing about which queries a name
    // can answer. Covers every reading at every position plus the separator when a boundary exists.
    private static ulong GridCharMask(string name, IAliasGridProvider provider)
    {
        var mask = 0UL;
        var separator = AliasProviderRegistry.GetSyllableSeparator(AliasProviderRegistry.GetProviderId(provider));
        var sets = provider.GetReadingSets(name);
        for (var i = 0; i < name.Length && i < sets.Length; i++)
        {
            var readings = sets[i];
            if (readings == null)
                continue;
            foreach (var reading in readings)
                mask |= FzfAlgorithm.GetCharMask(reading);

            if (separator != '\0' && provider.NeedsSeparatorBefore(name, i))
                mask |= FzfAlgorithm.GetCharMask(separator.ToString());
        }
        return mask;
    }
}
