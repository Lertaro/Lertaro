using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Core.IndexV2.Alias;

// Storage encoding for the per-position reading grid a provider offers through IAliasGridProvider:
// the encoder, and the reusable decode index AliasGridMatcher walks.
//
// A name's alias space is the cross product of its per-position readings -- a 40-character name whose
// every character has two readings has 2^40 spellings -- so nothing may ever enumerate it. This format
// stores the space instead: one reading set per position, plus one boundary bit per position. A
// spelling is a choice of one reading per position, made while walking a query term, never in advance.
// Size is therefore linear in the name's length and independent of how polyphonic that name is.
//
// Two payload shapes, one per storage site:
//
//   * FormatTag (1) -- SELF-CONTAINED: every reading is spelled out in UTF-16. Used by the delta overlay
//     and by the live tiers, which encode a grid from a provider for one text and decode it immediately:
//     there is no snapshot-wide vocabulary to reference, and the payload never reaches a file.
//   * FormatTagReadings (2) -- TABLE-BACKED: a reading is either one literal ASCII code unit (one byte:
//     the passed-through characters most positions of a long name are) or an id into the snapshot's
//     AliasReadings section (see AliasReadingTable), which is what turns a syllable from 7-9 bytes per
//     occurrence into one or two.
//
// Readings are UTF-16 code units, not UTF-8, in the self-contained shape for the same reason
// PinyinAliasUtf8PinyinEncoder.AppendLiteralChar re-pairs surrogate halves before encoding: an alias
// string is UTF-16 and may contain a lone surrogate half in the middle of a name (an emoji or a CJK
// extension-B character passes through literally). Byte encodings cannot represent one, so a UTF-8
// payload would corrupt exactly those names. It also lets the decoder hand the matcher char spans
// without a decode step. (The table-backed shape inherits the same property from the table.)
//
// Layout (little-endian; counts are varints):
//   byte          FormatTag (1) or FormatTagReadings (2)
//   varint        PositionCount
//   per position: byte ReadingCount, then per reading:
//                   shape 1: varint CharCount + CharCount * 2 bytes (UTF-16 units)
//                   shape 2: one literal ASCII unit (a byte < 0x80), or a table id (see TryReadReading)
//   ceil(PositionCount / 8) bytes of boundary bits: bit i set = SyllableSeparator before position i
//
// The tag is load-bearing: the payload sits in the snapshot's alias blob as opaque bytes, so a misread
// payload must fail at decode instead of walking off into whatever the next section holds.
internal static class AliasGridFormat
{
    public const byte FormatTag = 1;

    // Readings referenced by id into AliasReadingTable (the snapshot's AliasReadings section).
    public const byte FormatTagReadings = 2;

    // Windows caps a file name at 255 characters, and the grid exists to handle long names, so this
    // guard sits far above any legal name: more positions than this means corruption, not a name.
    internal const int MaxPositions = 4096;

    // Readings per position and units per reading are provider data, not attacker input, but a corrupt
    // payload decodes through the same path -- so both are bounded and violation is a failed bind.
    internal const int MaxReadingsPerPosition = 255;
    internal const int MaxReadingUnits = 256;

    /// <summary>
    /// Encodes <paramref name="text"/>'s alias space as one sink segment and returns its byte length,
    /// which is what the caller weighs against the string aliases it would otherwise bake for this
    /// name. Returns 0 when the provider violates the interface contract; the caller then keeps the
    /// string encoding, which is always a correct (if larger) representation of the same alias space.
    /// </summary>
    public static int Encode(string text, IAliasGridProvider provider, AliasByteSink dest)
        => Encode(text, provider, dest, null);

    /// <summary>
    /// Encodes <paramref name="text"/>'s readings against <paramref name="table"/>: each reading becomes a
    /// table id, except a single ASCII code unit, which goes inline as one byte. Returns 0 (writing
    /// nothing) when a reading is missing from the table, which means the snapshot's vocabulary was
    /// collected from a different set of names than this payload is being written for.
    /// </summary>
    public static int Encode(string text, IAliasGridProvider provider, AliasByteSink dest, AliasReadingTable? table)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxPositions)
            return 0;

        var sets = provider.GetReadingSets(text);
        if (sets.Length < text.Length)
            return ContractViolation(text, "reading sets shorter than the text");

        Span<byte> readingBytes = stackalloc byte[MaxReadingUnits * 2];
        var boundaryBytes = (text.Length + 7) / 8;
        Span<byte> boundaries = stackalloc byte[(MaxPositions + 7) / 8];
        boundaries[..boundaryBytes].Clear();

        var start = dest.BeginSegment();
        dest.Append(table == null ? FormatTag : FormatTagReadings);
        AppendVarint(dest, text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var readings = sets[i];
            if (readings == null || readings.Length == 0 || readings.Length > MaxReadingsPerPosition)
                return Abandon(text, dest, start, $"position {i} has {readings?.Length ?? 0} readings");

            dest.Append((byte)readings.Length);
            foreach (var reading in readings)
            {
                if (string.IsNullOrEmpty(reading) || reading.Length > MaxReadingUnits)
                    return Abandon(text, dest, start, $"position {i} has a {reading?.Length ?? 0}-unit reading");

                if (table != null)
                {
                    if (!AliasReadingTable.IsEligible(reading))
                    {
                        dest.Append((byte)reading[0]);
                        continue;
                    }

                    if (!table.TryGetId(reading, out var id))
                        return Abandon(text, dest, start, $"position {i} reads '{reading}', which is not in the snapshot's reading table");

                    AppendReadingId(dest, id);
                    continue;
                }

                for (var u = 0; u < reading.Length; u++)
                {
                    var value = (ushort)reading[u];
                    readingBytes[u * 2] = (byte)value;
                    readingBytes[u * 2 + 1] = (byte)(value >> 8);
                }

                AppendVarint(dest, reading.Length);
                dest.Append(readingBytes[..(reading.Length * 2)]);
            }

            if (provider.NeedsSeparatorBefore(text, i))
                boundaries[i >> 3] |= (byte)(1 << (i & 7));
        }

        dest.Append(boundaries[..boundaryBytes]);
        dest.EndSegment(start);
        return dest.Segment(dest.SegmentCount - 1).Length;
    }

    private static int ContractViolation(string text, string reason)
    {
        Logger.Log($"[AliasGrid] {reason} for '{text[..Math.Min(text.Length, 32)]}'; keeping string aliases.", LogLevel.Error);
        return 0;
    }

    private static int Abandon(string text, AliasByteSink dest, int start, string reason)
    {
        dest.AbandonSegment(start);
        return ContractViolation(text, reason);
    }

    internal static void AppendVarint(AliasByteSink dest, int value)
    {
        while (value >= 0x80)
        {
            dest.Append((byte)(value | 0x80));
            value >>= 7;
        }
        dest.Append((byte)value);
    }

    // A table id: the first byte marks "this is an id, not a literal" with its high bit and carries the
    // id's low six bits; bit 6 says more bytes follow. The most common readings therefore cost one byte.
    private static void AppendReadingId(AliasByteSink dest, int id)
    {
        var low = id & 0x3F;
        var rest = id >> 6;
        dest.Append((byte)(0x80 | (rest > 0 ? 0x40 : 0) | low));
        while (rest > 0)
        {
            var b = rest & 0x7F;
            rest >>= 7;
            dest.Append((byte)(rest > 0 ? b | 0x80 : b));
        }
    }

    internal static bool TryReadVarint(ReadOnlySpan<byte> payload, ref int offset, out int value)
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

    /// <summary>
    /// Reads one reading code of a table-backed payload: either a literal ASCII unit or a table id.
    /// </summary>
    private static bool TryReadReading(ReadOnlySpan<byte> payload, ref int offset, out int literal, out int id)
    {
        literal = -1;
        id = -1;
        if (offset >= payload.Length)
            return false;

        var b = payload[offset++];
        if (b < 0x80)
        {
            literal = b;
            return true;
        }

        var value = b & 0x3F;
        var shift = 6;
        if ((b & 0x40) != 0)
        {
            while (true)
            {
                if (offset >= payload.Length || shift > 28)
                    return false;
                var c = payload[offset++];
                value |= (c & 0x7F) << shift;
                shift += 7;
                if ((c & 0x80) == 0)
                    break;
            }
        }

        id = value;
        return true;
    }

    /// <summary>
    /// Reusable decode index over one payload: per-position reading ranges plus the boundary bits, and
    /// either the payload's own characters or the reading table it references (so the matcher walks char
    /// spans and never re-parses). Thread-static at the call site; a bind allocates nothing once the
    /// scratch has grown. One instance is valid for exactly one payload at a time.
    /// </summary>
    internal sealed class Index
    {
        private int[] _firstReading = new int[64];
        private byte[] _readingCount = new byte[64];
        private int[] _readingCharStart = new int[64];
        private int[] _readingCharLength = new int[64];
        // Table-backed payloads: the table id of each reading, or -1 when the reading is a literal that
        // was widened into _chars (which is also every reading of a self-contained payload).
        private int[] _readingId = CreateReadingIds(64);
        private char[] _chars = new char[256];
        private ulong[] _boundaries = new ulong[4];
        private int _charCount;
        private AliasReadingTable? _table;
        // The bitmap of code units 0..127 that some reading of this grid starts with, plus the flag for a
        // reading that starts above that range. See StartsFrom / CoversInitials.
        private ulong _firstLow;
        private ulong _firstHigh;
        private bool _firstWide;

        private static int[] CreateReadingIds(int length)
        {
            var ids = new int[length];
            Array.Fill(ids, -1);
            return ids;
        }

        public int PositionCount { get; private set; }
        public int TotalReadings { get; private set; }

        /// <summary>Binds a self-contained payload (delta rows and the live tiers).</summary>
        public bool TryBind(ReadOnlySpan<byte> payload) => TryBind(payload, null);

        /// <summary>
        /// Binds a payload, which may reference <paramref name="table"/> -- the snapshot's reading
        /// vocabulary. A table-backed payload without one cannot be decoded and fails to bind.
        /// </summary>
        public bool TryBind(ReadOnlySpan<byte> payload, AliasReadingTable? table)
        {
            PositionCount = 0;
            TotalReadings = 0;
            _table = null;
            _firstLow = 0;
            _firstHigh = 0;
            _firstWide = false;
            if (payload.Length < 3)
                return false;

            var tableBacked = payload[0] switch
            {
                FormatTag => false,
                FormatTagReadings => true,
                _ => (bool?)null,
            };
            if (tableBacked == null || (tableBacked.Value && (table == null || table.Count == 0)) )
                return false;

            var offset = 1;
            if (!TryReadVarint(payload, ref offset, out var positions) || positions <= 0 || positions > MaxPositions)
                return false;

            EnsurePositions(positions);
            _charCount = 0;
            _firstLow = 0;
            _firstHigh = 0;
            _firstWide = false;
            var reading = 0;

            for (var i = 0; i < positions; i++)
            {
                if (offset >= payload.Length)
                    return false;
                var count = payload[offset++];
                if (count == 0)
                    return false;

                _firstReading[i] = reading;
                _readingCount[i] = count;
                EnsureReadings(reading + count);

                for (var k = 0; k < count; k++)
                {
                    if (tableBacked.Value)
                    {
                        if (!TryReadReading(payload, ref offset, out var literal, out var id))
                            return false;

                        if (id >= 0)
                        {
                            if (id >= table!.Count)
                                return false;
                            var idReading = table.Reading(id);
                            if (idReading.Length > 0)
                                NoteFirst(idReading[0]);
                            _readingId[reading] = id;
                            _readingCharStart[reading] = 0;
                            _readingCharLength[reading] = 0;
                            reading++;
                            continue;
                        }

                        // A literal unit: widened into _chars so Reading() can hand out a span either way.
                        EnsureChars(_charCount + 1);
                        _chars[_charCount] = (char)literal;
                        NoteFirst((char)literal);
                        _readingId[reading] = -1;
                        _readingCharStart[reading] = _charCount;
                        _readingCharLength[reading] = 1;
                        _charCount++;
                        reading++;
                        continue;
                    }

                    if (!TryReadVarint(payload, ref offset, out var units) || units <= 0 || units > MaxReadingUnits)
                        return false;
                    if (offset + units * 2 > payload.Length)
                        return false;

                    EnsureChars(_charCount + units);
                    for (var u = 0; u < units; u++)
                    {
                        var lo = payload[offset + u * 2];
                        var hi = payload[offset + u * 2 + 1];
                        _chars[_charCount + u] = (char)(lo | (hi << 8));
                    }

                    NoteFirst(_chars[_charCount]);
                    _readingId[reading] = -1;
                    _readingCharStart[reading] = _charCount;
                    _readingCharLength[reading] = units;
                    _charCount += units;
                    offset += units * 2;
                    reading++;
                }
            }

            var boundaryBytes = (positions + 7) / 8;
            if (offset + boundaryBytes > payload.Length)
                return false;

            var words = (boundaryBytes + 7) / 8;
            if (_boundaries.Length < words)
                _boundaries = new ulong[Math.Max(words, _boundaries.Length * 2)];
            Array.Clear(_boundaries, 0, words);
            for (var b = 0; b < boundaryBytes; b++)
                _boundaries[b >> 3] |= (ulong)payload[offset + b] << ((b & 7) * 8);

            PositionCount = positions;
            TotalReadings = reading;
            _table = table;
            return true;
        }

        public int ReadingCount(int position) => _readingCount[position];

        public ReadOnlySpan<char> Reading(int position, int index)
        {
            var r = _firstReading[position] + index;
            var id = _readingId[r];
            // The id guard is belt and braces: every choice the matcher makes is an index this bind wrote,
            // but a stale one from an earlier payload must answer a stale reading rather than walk off the
            // end of the table or a null one.
            return id >= 0 && _table != null && id < _table.Count
                ? _table.Reading(id)
                : _chars.AsSpan(_readingCharStart[r], _readingCharLength[r]);
        }

        public bool BoundaryBefore(int position) => (_boundaries[position >> 6] & (1UL << (position & 63))) != 0;

        private void NoteFirst(char c)
        {
            if (c < 64)
                _firstLow |= 1UL << c;
            else if (c < 128)
                _firstHigh |= 1UL << (c - 64);
            else
                _firstWide = true;
        }

        /// <summary>
        /// Whether some reading of this grid starts with <paramref name="c"/>. Every piece a term is made of
        /// is a prefix of a reading, so it starts where a reading does -- which lets a term whose first
        /// letter no syllable of this name can begin with be refused without visiting a single position.
        /// Answers true when some reading starts above the tracked range: the bitmap cannot then say "no",
        /// so the walk is asked instead.
        /// </summary>
        public bool StartsFrom(char c)
        {
            if (_firstWide)
                return true;
            if (c >= 128)
                return false;
            return c < 64
                ? (_firstLow & (1UL << c)) != 0
                : (_firstHigh & (1UL << (c - 64))) != 0;
        }

        /// <summary>
        /// Whether every unit of <paramref name="term"/> can start a reading -- what the one-letter-per-
        /// position tier needs of a whole term at once.
        /// </summary>
        public bool StartsFromAll(ReadOnlySpan<char> term)
        {
            for (var i = 0; i < term.Length; i++)
                if (!StartsFrom(term[i]))
                    return false;
            return true;
        }

        private void EnsurePositions(int count)
        {
            if (_firstReading.Length >= count)
                return;
            // Array.Resize, not a fresh array: this runs per position DURING a bind, so replacing the
            // array would discard the offsets already written for the positions before it.
            Array.Resize(ref _firstReading, Math.Max(count, _firstReading.Length * 2));
            Array.Resize(ref _readingCount, _firstReading.Length);
        }

        private void EnsureReadings(int count)
        {
            if (_readingCharStart.Length >= count)
                return;

            // Only the slots the resize ADDS get the "no table id yet" default: filling from the front
            // would erase the ids of the readings this same bind already decoded.
            var previous = _readingId.Length;
            Array.Resize(ref _readingCharStart, Math.Max(count, _readingCharStart.Length * 2));
            Array.Resize(ref _readingCharLength, _readingCharStart.Length);
            Array.Resize(ref _readingId, _readingCharStart.Length);
            Array.Fill(_readingId, -1, previous, _readingId.Length - previous);
        }

        private void EnsureChars(int count)
        {
            if (_chars.Length >= count)
                return;
            Array.Resize(ref _chars, Math.Max(count, _chars.Length * 2));
        }
    }
}
