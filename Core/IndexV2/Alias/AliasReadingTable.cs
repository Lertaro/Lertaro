using System.Collections.Concurrent;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.IndexV2.Alias;

// The reading vocabulary one snapshot's grids are written against: every reading that a payload cannot
// carry inline lives here once, and the payloads reference it by id.
//
// That is where a reading's bytes stop being repeated per position per name. Today a position spells its
// reading out in UTF-16 (a 3-4 character syllable costs 1 byte of length + 6-8 bytes of text, on every
// position of every name that spells 学), while an id costs one or two bytes and the text exists once per
// snapshot. Measured on the query-time harness's 1201-name corpus (scratch\pinmeasure): the alias sections
// go from 37,761 B to 17,083 B of payload plus a 1,261 B table, i.e. 0.49x, and the whole snapshot drops
// 10.8%. Same names, same queries, same answers -- recall and result rows are bit-identical to before.
//
// A single ASCII code unit is deliberately NOT an entry: the passed-through characters that make up most
// positions of a long name are exactly those ("第01集" is four syllables and one such position each), and
// an id would add an entry while saving nothing, since AliasGridFormat writes that unit inline as one
// byte. Everything else -- a syllable, a lone surrogate, a rare unmapped character -- is shared.
//
// Ids are assigned by ordinal sort of the reading text, so re-baking the same names produces the same
// bytes; the alias pre-pass was written to keep rebuilds byte-identical (see SnapshotWriter) and this is
// the one part of it that could quietly break that.
internal sealed class AliasReadingTable
{
    private char[] _chars = [];
    private int[] _start = [];
    private int[] _length = [];
    // Writer-side only: reading text -> id, for the encoder. A table bound from a payload never needs it.
    private Dictionary<string, int>? _ids;

    public int Count { get; private set; } = 0;

    /// <summary>A table with no entries, for a snapshot whose provider produced none.</summary>
    public static readonly AliasReadingTable Empty = new();

    public ReadOnlySpan<char> Reading(int id) => _chars.AsSpan(_start[id], _length[id]);

    /// <summary>
    /// Whether a payload must reference this reading by id rather than carry it inline: anything that is
    /// not exactly one ASCII code unit.
    /// </summary>
    public static bool IsEligible(ReadOnlySpan<char> reading)
        => reading.Length != 1 || reading[0] >= 0x80;

    /// <summary>The id of <paramref name="reading"/>, for the encoder that built this table.</summary>
    internal bool TryGetId(ReadOnlySpan<char> reading, out int id)
    {
        if (_ids == null)
        {
            id = -1;
            return false;
        }

        var lookup = _ids.GetAlternateLookup<ReadOnlySpan<char>>();
        return lookup.TryGetValue(reading, out id);
    }

    /// <summary>
    /// Binds the table payload <see cref="Builder.WritePayload"/> wrote. Read-only afterwards, so one
    /// instance serves every thread and every query on its snapshot.
    /// </summary>
    public bool TryBind(ReadOnlySpan<byte> payload)
    {
        Count = 0;
        var offset = 0;
        // An entry costs at least two bytes (its width flag and its unit count), so a count that large can
        // only come from corruption -- and must not become an allocation the payload cannot possibly fill.
        if (!AliasGridFormat.TryReadVarint(payload, ref offset, out var entries)
            || entries < 0 || entries > AliasGridFormat.MaxReadingsPerPosition * AliasGridFormat.MaxPositions
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
            if (ascii > 1 || !AliasGridFormat.TryReadVarint(payload, ref offset, out var units) || units <= 0 || units > AliasGridFormat.MaxReadingUnits)
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
        _ids = null;
        Count = entries;
        return true;
    }

    /// <summary>
    /// Collects the eligible readings of a whole snapshot, then freezes them into a table in ordinal
    /// order. One instance per rebuild; <see cref="Add"/> is called from the alias pre-pass's threads.
    /// </summary>
    internal sealed class Builder
    {
        private readonly ConcurrentDictionary<string, byte> _readings = new(StringComparer.Ordinal);

        public void Add(string reading)
        {
            if (IsEligible(reading))
                _readings.TryAdd(reading, 0);
        }

        public AliasReadingTable Build()
        {
            var ordered = _readings.Keys.ToArray();
            Array.Sort(ordered, StringComparer.Ordinal);

            var table = new AliasReadingTable
            {
                _ids = new Dictionary<string, int>(ordered.Length, StringComparer.Ordinal),
                _start = new int[ordered.Length],
                _length = new int[ordered.Length],
            };

            var chars = new char[Math.Max(16, ordered.Length * 4)];
            var charCount = 0;
            for (var i = 0; i < ordered.Length; i++)
            {
                var reading = ordered[i];
                if (chars.Length < charCount + reading.Length)
                    Array.Resize(ref chars, Math.Max(charCount + reading.Length, chars.Length * 2));

                reading.CopyTo(0, chars, charCount, reading.Length);
                table._ids[reading] = i;
                table._start[i] = charCount;
                table._length[i] = reading.Length;
                charCount += reading.Length;
            }

            table._chars = chars;
            table.Count = ordered.Length;
            return table;
        }
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
        AliasGridFormat.AppendVarint(dest, Count);
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
            AliasGridFormat.AppendVarint(dest, reading.Length);
            foreach (var c in reading)
            {
                dest.Append((byte)c);
                if (!asciiOnly)
                    dest.Append((byte)(c >> 8));
            }
        }
        dest.EndSegment(start);
    }
}
