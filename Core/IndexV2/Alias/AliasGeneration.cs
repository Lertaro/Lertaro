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
// A provider that implements IAliasGridProvider can have its reading-derived aliases stored as a grid
// instead of spelled out; the choice is made per name by PreferGrid, which both this string path and the
// snapshot's byte path go through. That is what keeps a delta row and the same row after compaction
// answering a query identically -- the two paths differ only in where the payload is put.
internal static class AliasGeneration
{
    [ThreadStatic] private static AliasByteSink? _readingSink;
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

                foreach (var alias in provider.GetAliases(name))
                {
                    if (string.IsNullOrWhiteSpace(alias))
                        continue;
                    (aliases ??= new List<string>()).Add(alias.ToLowerInvariant());
                    (ids ??= new List<byte>()).Add(providerId);
                }
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

    // A grid-capable provider's entries go through its byte-side split API rather than GetAliases: the
    // reading aliases and the standalone aliases have to be distinguishable, because only the former may
    // be replaced by the grid. Both halves are the same bytes the snapshot bakes.
    private static void GenerateForGridProvider(
        string name,
        IAliasGridProvider provider,
        byte providerId,
        ref List<string>? aliases,
        ref List<byte>? ids,
        ref List<GridAlias>? grids)
    {
        var readingSink = _readingSink ??= new AliasByteSink();
        var gridSink = _gridSink ??= new AliasByteSink();
        if (PreferGrid(name, provider, readingSink, gridSink, out _))
            (grids ??= new List<GridAlias>()).Add(new GridAlias(providerId, gridSink.Segment(0).ToArray()));
        else
            AppendStrings(readingSink, providerId, ref aliases, ref ids);

        var standalone = _standaloneSink ??= new AliasByteSink();
        standalone.Reset();
        provider.GetStandaloneAliasesUtf8(name, standalone);
        AppendStrings(standalone, providerId, ref aliases, ref ids);
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
    /// Fills <paramref name="readingSink"/> with the provider's spelled-out reading aliases and
    /// <paramref name="gridSink"/> with their grid encoding, then reports whether the grid is the
    /// smaller of the two and should be stored instead.
    /// </summary>
    /// <remarks>
    /// "Smaller" is a per-name comparison, not a policy: a name with one reading per position spells out
    /// to a handful of bytes and keeps them, while a polyphonic name's spelled-out form is an
    /// enumeration of its readings' cross product and loses. Both sinks belong to the caller and are
    /// reset here.
    /// </remarks>
    internal static bool PreferGrid(string name, IAliasGridProvider provider, AliasByteSink readingSink, AliasByteSink gridSink, out int gridLength)
    {
        readingSink.Reset();
        gridSink.Reset();
        provider.GetReadingAliasesUtf8(name, readingSink);
        gridLength = AliasGridFormat.Encode(name, provider, gridSink);
        if (gridLength <= 0)
            return false;

        var readingBytes = 0;
        for (var s = 0; s < readingSink.SegmentCount; s++)
            readingBytes += readingSink.Segment(s).Length;

        return gridLength < readingBytes;
    }
}
