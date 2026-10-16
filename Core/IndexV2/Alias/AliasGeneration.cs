using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.IndexV2.Alias;

/// <summary>
/// One grid-shaped alias entry: a provider id and the payload
/// <see cref="AliasGridFormat"/> encoded for that name. Used by the delta overlay, whose rows are
/// generated in memory and so cannot address the snapshot's alias sections.
/// </summary>
internal readonly record struct GridAlias(byte ProviderId, byte[] Payload);

// Alias generation for snapshot building and delta rows -- same providers, same lowercasing, same
// null-for-ASCII fast path as the name search that reads them. Aliases derive from the name alone, so
// they bake per unique name at build time and are
// filtered per provider id at QUERY time (SearchContext.DisabledAliasIds) -- toggling a provider in
// settings never requires a rebuild.
//
// Uses GetAllProviders() (every INSTALLED provider), not GetActiveProviders() (only ENABLED ones):
// generation must not depend on the enabled/disabled toggle, or re-enabling a provider the user
// disabled earlier would surface no aliases until the next rebuild. Baking in a disabled provider's
// aliases anyway costs a little extra CPU/storage at generation time, but makes every enable/disable
// toggle a free, instant, purely query-time flip in both directions.
//
// A provider that implements IAliasGridProvider has its reading-derived aliases stored as a grid. Both
// this string path and the snapshot's byte path go through TryEncodeGrid, which is what keeps a delta row
// and the same row after compaction answering a query identically -- the two paths differ only in where
// the payload is put.
internal static class AliasGeneration
{
    [ThreadStatic] private static AliasByteSink? _gridSink;
    [ThreadStatic] private static AliasByteSink? _standaloneSink;

    public static string[]? Generate(string name, out byte[] providerIds)
        => Generate(name, out providerIds, out _);

    public static string[]? Generate(string name, out byte[] providerIds, out GridAlias[] grids)
    {
        providerIds = Array.Empty<byte>();
        grids = Array.Empty<GridAlias>();
        if (string.IsNullOrEmpty(name) || AliasProviderRegistry.HasInvalidUtf16(name) || !AliasProviderRegistry.HasNonAscii(name))
            return null;

        List<string>? aliases = null;
        List<byte>? ids = null;
        List<GridAlias>? gridList = null;
        foreach (var provider in AliasProviderRegistry.GetAllProviders())
        {
            try
            {
                if (!provider.CanHandle(name))
                    continue;
                var providerId = AliasProviderRegistry.GetProviderId(provider);
                if (provider is IAliasGridProvider gridProvider)
                {
                    GenerateForGridProvider(name, gridProvider, providerId, ref aliases, ref ids, ref gridList);
                    continue;
                }

                AppendStrings(provider, name, providerId, ref aliases, ref ids);
            }
            catch (Exception ex)
            {
                Logger.Log($"[IndexV2] Alias provider failed for '{name}': {ex.Message}", LogLevel.Error);
            }
        }

        if (aliases == null && gridList == null)
            return null;

        providerIds = ids?.ToArray() ?? Array.Empty<byte>();
        grids = gridList?.ToArray() ?? Array.Empty<GridAlias>();
        return aliases?.ToArray() ?? Array.Empty<string>();
    }

    // A grid-capable provider's reading aliases go through its byte-side split API rather than GetAliases:
    // the reading aliases and the standalone aliases have to be distinguishable, because only the former
    // may be replaced by the grid.
    private static void GenerateForGridProvider(
        string name,
        IAliasGridProvider provider,
        byte providerId,
        ref List<string>? aliases,
        ref List<byte>? ids,
        ref List<GridAlias>? grids)
    {
        var gridSink = _gridSink ??= new AliasByteSink();
        gridSink.Reset();
        if (TryEncodeGrid(name, provider, gridSink, out _))
        {
            (grids ??= new List<GridAlias>()).Add(new GridAlias(providerId, gridSink.Segment(0).ToArray()));
        }
        else
        {
            // The provider could not express its readings as a grid, which is a contract violation
            // (AliasGridFormat reports and logs it). Its alias strings are then the only correct
            // representation of the same space, so all of them go out -- standalone ones included.
            AppendStrings(provider, name, providerId, ref aliases, ref ids);
            return;
        }

        var standalone = _standaloneSink ??= new AliasByteSink();
        standalone.Reset();
        provider.GetStandaloneAliasesUtf8(name, standalone);
        AppendStrings(standalone, providerId, ref aliases, ref ids);
    }

    private static void AppendStrings(IAliasProvider provider, string name, byte providerId, ref List<string>? aliases, ref List<byte>? ids)
    {
        foreach (var alias in provider.GetAliases(name))
        {
            if (string.IsNullOrWhiteSpace(alias))
                continue;
            (aliases ??= new List<string>()).Add(alias.ToLowerInvariant());
            (ids ??= new List<byte>()).Add(providerId);
        }
    }

    private static void AppendStrings(AliasByteSink sink, byte providerId, ref List<string>? aliases, ref List<byte>? ids)
    {
        for (var s = 0; s < sink.SegmentCount; s++)
        {
            var alias = System.Text.Encoding.UTF8.GetString(sink.Segment(s)).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(alias))
                continue;
            (aliases ??= new List<string>()).Add(alias);
            (ids ??= new List<byte>()).Add(providerId);
        }
    }

    /// <summary>
    /// Encodes <paramref name="name"/>'s readings as one grid segment in <paramref name="dest"/>, and
    /// reports whether it did; a provider that cannot express them leaves nothing behind.
    /// </summary>
    /// <remarks>
    /// The grid is how a grid-capable provider's reading space is stored, always -- there is no size
    /// comparison against its spelled-out aliases any more. That enumeration is the same space CUT OFF:
    /// the pinyin provider stops at 32 combinations and at 256 characters per variant, and a name that
    /// overruns either one falls back to a single reading per character, alphabetically first. 市's first
    /// reading is "fu", so the 184-character ticket name could not be found by "chuangyishiji" or
    /// "chuangysji" -- 创意市集 was baked as "chuang|yi|fu|ji" -- while every reading is one grid walk
    /// away. Measured cost of always taking the grid: ~1.4x the spelled-out bytes on monophonic names,
    /// and FEWER on polyphonic ones (0.68x/0.76x on two measured names), which is the same order the two
    /// encodings were already within.
    /// </remarks>
    internal static bool TryEncodeGrid(string name, IAliasGridProvider provider, AliasByteSink dest, out int length)
        => TryEncodeGrid(name, provider, null, dest, out length);

    /// <inheritdoc cref="TryEncodeGrid(string, IAliasGridProvider, AliasByteSink, out int)"/>
    /// <param name="table">
    /// The snapshot's reading vocabulary, for the snapshot build path: payloads then reference readings by
    /// id instead of spelling them out (see AliasReadingTable). Null -- the delta overlay and the live
    /// tiers -- encodes a self-contained payload, since there is no snapshot-wide table to point into.
    /// </param>
    internal static bool TryEncodeGrid(string name, IAliasGridProvider provider, AliasReadingTable? table, AliasByteSink dest, out int length)
        => (length = AliasGridFormat.Encode(name, provider, dest, table)) > 0;
}
