using System.Globalization;
using System.Text.Json;
using Lertaro.Core.IndexV2.Space;

namespace Lertaro.Cli.Space;

/// <summary>Serializes indexed sizes as strings so JScript never loses 64-bit precision.</summary>
internal static class SpaceEntriesJsonFormatter
{
    public static string Serialize(IReadOnlyList<SpaceIndexEntry> entries) => JsonSerializer.Serialize(entries.Select(entry => new
    {
        entry.Path,
        Size = entry.Size.ToString(CultureInfo.InvariantCulture)
    }));
}
