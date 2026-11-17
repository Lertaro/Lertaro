using System.Globalization;
using System.Text;
using System.Text.Json;
using Lertaro.Core.IndexV2.Space;

namespace Lertaro.Cli.Space;

/// <summary>Serializes indexed sizes as strings so JScript never loses 64-bit precision.</summary>
internal static class SpaceEntriesJsonFormatter
{
    public static string Serialize(IReadOnlyList<SpaceIndexEntry> entries) =>
        JsonSerializer.Serialize(entries.Select(entry => new
        {
            entry.Path,
            Size = entry.Size.ToString(CultureInfo.InvariantCulture)
        }));

    // Directory Opus only needs name -> size. Sorting by name makes the long package-directory prefixes
    // shared by neighbouring WinSxS entries cheap to front-code, avoiding a multi-megabyte JSON response.
    public static string SerializeFolderSizes(IReadOnlyList<SpaceIndexEntry> entries)
    {
        var builder = new StringBuilder(entries.Count * 48);
        var previous = string.Empty;
        foreach (var entry in entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
        {
            var shared = 0;
            var limit = Math.Min(previous.Length, entry.Name.Length);
            while (shared < limit && previous[shared] == entry.Name[shared])
                shared++;

            builder.Append(shared.ToString(CultureInfo.InvariantCulture))
                .Append('\t')
                .Append(entry.Size.ToString(CultureInfo.InvariantCulture))
                .Append('\t')
                .Append(entry.Name.AsSpan(shared))
                .Append('\n');
            previous = entry.Name;
        }

        return builder.ToString();
    }
}
