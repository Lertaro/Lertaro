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
// Readings are stored as UTF-16 code units, not UTF-8, for the same reason
// PinyinAliasUtf8PinyinEncoder.AppendLiteralChar re-pairs surrogate halves before encoding: an alias
// string is UTF-16 and may contain a lone surrogate half in the middle of a name (an emoji or a CJK
// extension-B character passes through literally). Byte encodings cannot represent one, so a UTF-8
// payload would corrupt exactly those names. It also lets the decoder hand the matcher char spans
// without a decode step.
//
// Layout (little-endian; counts are varints, lengths count UTF-16 code units):
//   byte          FormatTag
//   varint        PositionCount
//   per position: byte ReadingCount, then per reading: varint CharCount + CharCount * 2 bytes
//   ceil(PositionCount / 8) bytes of boundary bits: bit i set = SyllableSeparator before position i
//
// The tag is load-bearing: the payload sits in the snapshot's alias blob as opaque bytes, so a misread
// payload must fail at decode instead of walking off into whatever the next section holds.
internal static class AliasGridFormat
{
    public const byte FormatTag = 1;

    // Windows caps a file name at 255 characters, and the grid exists to handle long names, so this
    // guard sits far above any legal name: more positions than this means corruption, not a name.
    internal const int MaxPositions = 4096;

    // Readings per position and units per reading are provider data, not attacker input, but a corrupt
    // payload decodes through the same path -- so both are bounded and violation is a failed bind.
    internal const int MaxReadingsPerPosition = 255;
    private const int MaxReadingUnits = 256;

    /// <summary>
    /// Encodes <paramref name="text"/>'s alias space as one sink segment and returns its byte length,
    /// which is what the caller weighs against the string aliases it would otherwise bake for this
    /// name. Returns 0 when the provider violates the interface contract; the caller then keeps the
    /// string encoding, which is always a correct (if larger) representation of the same alias space.
    /// </summary>
    public static int Encode(string text, IAliasGridProvider provider, AliasByteSink dest)
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
        dest.Append(FormatTag);
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

    /// <summary>
    /// Reusable decode index over one payload: per-position reading ranges plus the boundary bits, and
    /// the payload's characters copied into one contiguous scratch buffer (so the matcher walks char
    /// spans and never re-parses). Thread-static at the call site; a bind allocates nothing once the
    /// scratch has grown. One instance is valid for exactly one payload at a time.
    /// </summary>
    internal sealed class Index
    {
        private int[] _firstReading = new int[64];
        private byte[] _readingCount = new byte[64];
        private int[] _readingCharStart = new int[64];
        private int[] _readingCharLength = new int[64];
        private char[] _chars = new char[256];
        private ulong[] _boundaries = new ulong[4];
        private int _charCount;

        public int PositionCount { get; private set; }
        public int TotalReadings { get; private set; }

        public bool TryBind(ReadOnlySpan<byte> payload)
        {
            PositionCount = 0;
            TotalReadings = 0;
            if (payload.Length < 3 || payload[0] != AliasGridFormat.FormatTag)
                return false;

            var offset = 1;
            if (!TryReadVarint(payload, ref offset, out var positions) || positions <= 0 || positions > MaxPositions)
                return false;

            EnsurePositions(positions);
            _charCount = 0;
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
            return true;
        }

        public int ReadingCount(int position) => _readingCount[position];

        public ReadOnlySpan<char> Reading(int position, int index)
        {
            var r = _firstReading[position] + index;
            return _chars.AsSpan(_readingCharStart[r], _readingCharLength[r]);
        }

        public bool BoundaryBefore(int position) => (_boundaries[position >> 6] & (1UL << (position & 63))) != 0;

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
            Array.Resize(ref _readingCharStart, Math.Max(count, _readingCharStart.Length * 2));
            Array.Resize(ref _readingCharLength, _readingCharStart.Length);
        }

        private void EnsureChars(int count)
        {
            if (_chars.Length >= count)
                return;
            Array.Resize(ref _chars, Math.Max(count, _chars.Length * 2));
        }
    }
}
