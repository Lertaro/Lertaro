using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.IndexV2.Alias;

// Byte-native twin of AliasGeneration for the snapshot bulk path: aliases arrive as UTF-8 segments
// via IAliasProvider.GetAliasesUtf8 (providers without a byte-native override fall back to their
// string API through the interface's default implementation, which also lowercases -- so the
// ToLowerInvariant step AliasGeneration does per alias string is already covered by contract).
// Same GetAllProviders/CanHandle/per-provider-try semantics as AliasGeneration.Generate; results
// are materialized into plain arrays so a parallel generation pass can hand them across threads
// to the strictly-serial snapshot blob writer.
//
// Every entry a provider contributes is an alias string. A provider whose readings are decided live
// (IAliasLiveMatcherProvider) bakes none of them, so the only thing left for it here is whatever else
// it emits -- the pinyin provider's Simplified form -- plus the reading letters its prefilter mask
// needs, which are taken from GetReadingSets and never stored.
internal static class AliasGenerationUtf8
{
    internal readonly struct Result
    {
        public readonly byte[] Bytes;
        public readonly int[] SegmentLengths;
        public readonly byte[] ProviderIds;

        public Result(byte[] bytes, int[] segmentLengths, byte[] providerIds)
        {
            Bytes = bytes;
            SegmentLengths = segmentLengths;
            ProviderIds = providerIds;
        }
    }

    [ThreadStatic] private static AliasByteSink? _sink;

    // Generates aliases for one name, OR-ing each alias's char mask into `mask`. Returns null when
    // the name yields no aliases (the common pure-ASCII case exits on the vectorized gate).
    public static Result? Generate(string name, ref ulong mask)
    {
        if (string.IsNullOrEmpty(name) || AliasProviderRegistry.HasInvalidUtf16(name) || !AliasProviderRegistry.HasNonAscii(name))
            return null;

        var sink = _sink ??= new AliasByteSink();
        sink.Reset();

        List<byte>? providerIds = null;
        foreach (var provider in AliasProviderRegistry.GetAllProviders())
        {
            try
            {
                if (!provider.CanHandle(name))
                    continue;
                var providerId = AliasProviderRegistry.GetProviderId(provider);
                AppendProvider(name, provider, providerId, sink, providerIds ??= new List<byte>(), ref mask);
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

        return new Result(bytes, segmentLengths, providerIds.ToArray());
    }

    // Appends one provider's aliases as string entries, plus -- for a provider that bakes no readings --
    // the letters of those readings, which the mask has to admit even though nothing stores them.
    private static void AppendProvider(
        string name,
        IAliasProvider provider,
        byte providerId,
        AliasByteSink dest,
        List<byte> providerIds,
        ref ulong mask)
    {
        // Without this the scan's char-mask gate would reject every CJK candidate for a pinyin query the
        // live matcher would have answered (a name's own chars carry none of the query's Latin letters).
        if (provider is IAliasLiveMatcherProvider liveProvider)
        {
            var sets = liveProvider.GetReadingSets(name);
            for (var i = 0; i < name.Length && i < sets.Length; i++)
            {
                var readings = sets[i];
                if (readings == null)
                    continue;
                foreach (var reading in readings)
                    mask |= FzfAlgorithm.GetCharMask(reading);
            }
        }

        var before = dest.SegmentCount;
        provider.GetAliasesUtf8(name, dest);
        RecordSegments(dest, before, providerId, providerIds, ref mask);
    }

    private static void RecordSegments(
        AliasByteSink dest,
        int from,
        byte providerId,
        List<byte> providerIds,
        ref ulong mask)
    {
        for (var s = from; s < dest.SegmentCount; s++)
        {
            providerIds.Add(providerId);
            mask |= FzfAlgorithm.GetCharMaskUtf8(dest.Segment(s));
        }
    }
}
