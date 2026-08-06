using System.Text;

namespace Lertaro.Core.Wire;

// Shared by SearchResultWithHighlightBinarySerializer's custom framing for a length-prefixed UTF-8
// string encoding using a 7-bit (LEB128-style) varint for the length, rather than a fixed-width int --
// most names/paths are short enough that this saves 2-3 bytes per string over a plain 32-bit length.
internal static class VarIntStringCodec
{
    public static void WriteString(Span<byte> buffer, ref int offset, string? str)
    {
        var s = str ?? string.Empty;
        var len = Encoding.UTF8.GetByteCount(s);
        Write7BitEncodedInt(buffer, ref offset, len);
        Encoding.UTF8.GetBytes(s, buffer.Slice(offset));
        offset += len;
    }

    public static void Write7BitEncodedInt(Span<byte> destination, ref int offset, int value)
    {
        var uValue = (uint)value;
        while (uValue >= 0x80)
        {
            destination[offset++] = (byte)(uValue | 0x80);
            uValue >>= 7;
        }
        destination[offset++] = (byte)uValue;
    }

    public static int Read7BitEncodedInt(byte[] buffer, ref int offset)
    {
        uint result = 0;
        var shift = 0;
        while (shift < 35)
        {
            var b = buffer[offset++];
            result |= (uint)(b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0)
                return (int)result;
        }
        throw new FormatException("Invalid 7-bit encoded integer.");
    }

    public static string ReadString(byte[] buffer, ref int offset)
    {
        var length = Read7BitEncodedInt(buffer, ref offset);
        if (length == 0) return string.Empty;
        var str = Encoding.UTF8.GetString(buffer, offset, length);
        offset += length;
        return str;
    }
}
