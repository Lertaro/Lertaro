using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.IndexV2.Alias;

// The snapshot's reading vocabulary: its <c>AliasReadings</c> section, one shared reading text per id.
//
// It was written for the per-name reading grids a snapshot used to bake: a grid payload could carry a
// reading either as one literal ASCII code unit or as an id into this table, which is what turned a
// syllable from 7-9 bytes per occurrence into one or two. Nothing bakes a grid any more -- a provider's
// readings are decided live (see <see cref="IAliasLiveMatcherProvider"/>) -- so the writer emits an EMPTY
// table, and a legacy snapshot's vocabulary is still bound and validated at open but read by nobody.
//
// The section stays because the file format stays. Real drives hold multi-gigabyte .idx files, a version
// bump means every one of them is re-indexed, and the offsets of the sections after this one are derived
// from its length -- so v12 keeps writing and reading it exactly as before.
internal sealed class AliasReadingTable
{
    private char[] _chars = [];
    private int[] _start = [];
    private int[] _length = [];

    // The bounds of this section's own encoding. Both are far above anything a real vocabulary reaches
    // (a few hundred readings, each a handful of characters), so a payload that needs them can only be
    // corruption -- and a corrupt count has to fail the bind before it becomes an allocation the payload
    // cannot possibly fill.
    private const int MaxEntries = 1_044_480;
    private const int MaxReadingUnits = 256;

    public int Count { get; private set; }

    /// <summary>A table with no entries: what the writer still emits for every snapshot.</summary>
    public static readonly AliasReadingTable Empty = new();

    public ReadOnlySpan<char> Reading(int id) => _chars.AsSpan(_start[id], _length[id]);

    /// <summary>
    /// Binds the table payload <see cref="WritePayload"/> wrote. Read-only afterwards, so one instance
    /// serves every thread and every query on its snapshot.
    /// </summary>
    public bool TryBind(ReadOnlySpan<byte> payload)
    {
        Count = 0;
        var offset = 0;
        // An entry costs at least two bytes (its width flag and its unit count), so a count that large can
        // only come from corruption -- and must not become an allocation the payload cannot possibly fill.
        if (!TryReadVarint(payload, ref offset, out var entries)
            || entries < 0 || entries > MaxEntries
            || entries > payload.Length / 2)
            return false;

        var start = new int[entries];
        var length = new int[entries];
        var chars = new char[16];
        var charCount = 0;
        for (var i = 0; i < entries; i++)
        {
            if (offset >= payload.Length)
                return false;

            var ascii = payload[offset++];
            if (ascii > 1 || !TryReadVarint(payload, ref offset, out var units) || units <= 0 || units > MaxReadingUnits)
                return false;

            var width = ascii == 1 ? 1 : 2;
            if (offset + units * width > payload.Length)
                return false;

            if (chars.Length < charCount + units)
                Array.Resize(ref chars, Math.Max(charCount + units, chars.Length * 2));

            for (var u = 0; u < units; u++)
            {
                chars[charCount + u] = ascii == 1
                    ? (char)payload[offset + u]
                    : (char)(payload[offset + u * 2] | (payload[offset + u * 2 + 1] << 8));
            }

            start[i] = charCount;
            length[i] = units;
            charCount += units;
            offset += units * width;
        }

        if (offset != payload.Length)
            return false;

        _chars = chars;
        _start = start;
        _length = length;
        Count = entries;
        return true;
    }

    /// <summary>
    /// Writes this table as the payload the snapshot stores in its <c>AliasReadings</c> section, and that
    /// <see cref="TryBind"/> reads back. Layout: varint entry count, then per entry a 1-byte width flag
    /// (1 = one byte per character, 0 = UTF-16 code units), a varint unit count and the units. The flag is
    /// not a saving for its own sake: almost every reading is a lowercase ASCII syllable, and one byte per
    /// character halves the table over storing code units.
    /// </summary>
    public void WritePayload(AliasByteSink dest)
    {
        var start = dest.BeginSegment();
        AppendVarint(dest, Count);
        for (var i = 0; i < Count; i++)
        {
            var reading = Reading(i);
            var asciiOnly = true;
            foreach (var c in reading)
            {
                if (c >= 0x80)
                {
                    asciiOnly = false;
                    break;
                }
            }

            dest.Append(asciiOnly ? (byte)1 : (byte)0);
            AppendVarint(dest, reading.Length);
            foreach (var c in reading)
            {
                dest.Append((byte)c);
                if (!asciiOnly)
                    dest.Append((byte)(c >> 8));
            }
        }
        dest.EndSegment(start);
    }

    // This section's varint codec: 7 bits per byte, high bit set while more bytes follow.
    private static void AppendVarint(AliasByteSink dest, int value)
    {
        while (value >= 0x80)
        {
            dest.Append((byte)(value | 0x80));
            value >>= 7;
        }
        dest.Append((byte)value);
    }

    private static bool TryReadVarint(ReadOnlySpan<byte> payload, ref int offset, out int value)
    {
        value = 0;
        var shift = 0;
        while (true)
        {
            if (offset >= payload.Length || shift > 28)
                return false;
            var b = payload[offset++];
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return true;
            shift += 7;
        }
    }
}
