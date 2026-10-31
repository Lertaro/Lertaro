using System.Text;

namespace Lertaro.PluginSdk.Helpers;

/// <summary>
/// Encoding detection shared by plain-text surfaces (preview, content indexing). The precedence is:
/// BOM (UTF-8 / UTF-16 LE/BE / UTF-32), BOM-less UTF-16 shape, strict UTF-8, then GB18030 (a GBK/GB2312
/// superset).
/// </summary>
public static class TextEncodingHelper
{
    /// <summary>Leading bytes inspected by <see cref="LooksBinary(ReadOnlySpan{byte})"/>.</summary>
    public const int BinaryProbeLength = 8192;

    static TextEncodingHelper() =>
        // GB18030 is not in the default .NET Core encoding set; the code pages provider ships in the
        // shared framework and only needs registering once per process.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string DecodeText(byte[] bytes) => DecodeText(bytes, bytes.Length, hasMoreBytes: false);

    /// <summary>
    /// Decodes up to <paramref name="length"/> bytes. <paramref name="hasMoreBytes"/> marks the buffer as a
    /// prefix of a longer file, so a multi-byte character cut at the buffer boundary is not taken as proof
    /// that the encoding is wrong.
    /// </summary>
    public static string DecodeText(byte[] bytes, int length, bool hasMoreBytes)
    {
        if (length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
            return DecodeChunk(Encoding.UTF32, bytes, length, 4, hasMoreBytes);
        if (length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            return DecodeChunk(new UTF32Encoding(bigEndian: true, byteOrderMark: false), bytes, length, 4, hasMoreBytes);
        if (length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return TryDecodeUtf8(bytes, 3, length, hasMoreBytes) ?? DecodeChunk(Encoding.UTF8, bytes, length, 3, hasMoreBytes);
        if (length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return DecodeChunk(Encoding.Unicode, bytes, length, 2, hasMoreBytes);
        if (length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return DecodeChunk(Encoding.BigEndianUnicode, bytes, length, 2, hasMoreBytes);

        // BOM-less UTF-16 is sniffed before the byte decoders because NUL is valid UTF-8 and GBK accepts
        // any byte pair, so neither would ever reject it.
        if (TrySniffBomlessUtf16(bytes.AsSpan(0, length), out var utf16))
            return DecodeChunk(utf16, bytes, length, 0, hasMoreBytes);

        // A UTF-8 file must never be misread as GB18030 (the two overlap on plain ASCII and nothing else
        // worth trusting), while GBK content almost always holds byte sequences that are invalid UTF-8.
        return TryDecodeUtf8(bytes, 0, length, hasMoreBytes) ?? DecodeChunk(Encoding.GetEncoding("GB18030"), bytes, length, 0, hasMoreBytes);
    }

    /// <summary>
    /// A NUL byte in the leading chunk is the standard binary tell (git and file(1) use the same), except
    /// for UTF-16 text, whose bytes are NUL-interleaved by design.
    /// </summary>
    public static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        var probe = bytes.Length > BinaryProbeLength ? bytes[..BinaryProbeLength] : bytes;
        if (HasUnicodeBom(probe)) return false;
        if (!probe.Contains((byte)0)) return false;
        return !TrySniffBomlessUtf16(probe, out _);
    }

    public static bool HasUnicodeBom(ReadOnlySpan<byte> bytes) =>
        (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00) ||
        (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF) ||
        (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ||
        (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)));

    // BOM-less UTF-16 has no marker, but its NUL bytes sit on one lane only (ASCII text puts a NUL on every
    // high byte in LE, every low byte in BE). Requiring the other lane to be nearly NUL-free keeps ordinary
    // binary files -- which have NULs spread across both lanes -- from matching.
    public static bool TrySniffBomlessUtf16(ReadOnlySpan<byte> bytes, out Encoding encoding)
    {
        encoding = Encoding.Unicode;
        if (bytes.Length < 8) return false;

        var pairs = bytes.Length / 2;
        var evenZeros = 0;
        var oddZeros = 0;
        for (var i = 0; i < pairs; i++)
        {
            if (bytes[2 * i] == 0) evenZeros++;
            if (bytes[2 * i + 1] == 0) oddZeros++;
        }

        var threshold = Math.Max(2, pairs / 3);
        if (oddZeros >= threshold && evenZeros * 20 <= oddZeros) return true;
        if (evenZeros >= threshold && oddZeros * 20 <= evenZeros)
        {
            encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: false);
            return true;
        }
        return false;
    }

    private static string? TryDecodeUtf8(byte[] bytes, int offset, int length, bool hasMoreBytes)
    {
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        // A buffer cut mid-character ends in 1-3 stray bytes; drop them before concluding it isn't UTF-8.
        var maxTrim = hasMoreBytes ? Math.Min(3, length - offset) : 0;
        for (var trim = 0; trim <= maxTrim; trim++)
        {
            try { return strict.GetString(bytes, offset, length - offset - trim); }
            catch (DecoderFallbackException) { }
        }
        return null;
    }

    private static string DecodeChunk(Encoding encoding, byte[] bytes, int length, int bomLength, bool hasMoreBytes)
    {
        var count = length - bomLength;
        // Same mid-character cut, for the fixed-width UTF-16/UTF-32 code units.
        if (hasMoreBytes)
        {
            var unitSize = encoding is UnicodeEncoding ? 2 : encoding is UTF32Encoding ? 4 : 1;
            count -= count % unitSize;
        }
        return encoding.GetString(bytes, bomLength, Math.Max(0, count));
    }
}
