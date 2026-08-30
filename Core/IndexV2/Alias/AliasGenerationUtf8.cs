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
                var before = sink.SegmentCount;
                provider.GetAliasesUtf8(name, sink);
                for (var s = before; s < sink.SegmentCount; s++)
                    (providerIds ??= new List<byte>()).Add(providerId);
            }
            catch (Exception ex)
            {
                Logger.Log($"[IndexV2] Alias provider failed for '{name}': {ex.Message}", LogLevel.Error);
            }
        }

        if (providerIds == null || sink.SegmentCount == 0)
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
            mask |= FzfAlgorithm.GetCharMaskUtf8(segment);
        }

        return new Result(bytes, segmentLengths, providerIds.ToArray());
    }
}
