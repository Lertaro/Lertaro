using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Lertaro.Plugins.CoreExtensions.Preview.Controls;

// MP4/MOV share the same ISO base media box structure. Phone cameras commonly store portrait video as
// landscape pixel data plus a rotation matrix in the "tkhd" box telling players to rotate it for display --
// MediaElement/Media Foundation ignores that matrix, so without reading it ourselves the video plays back
// sideways (see MediaPreviewControl.Player_MediaOpened, the only caller of this).
//
// Every read here is bounds-checked against the box's own declared range rather than trusted blindly: this
// walks arbitrary bytes from a file that only needs a matching extension to reach it, so a renamed,
// truncated, or deliberately malformed "video" must fail closed (rotation 0, i.e. today's unrotated
// behavior) instead of throwing, looping forever, or reading past the file.
internal static class Mp4RotationReader
{
    // Each box is at least 8 bytes, so this bounds worst-case iterations even for an adversarial file full
    // of minimum-size boxes -- generous enough for any real container (which has a handful per level).
    private const int MaxBoxesPerLevel = 10_000;

    public static int GetRotationDegrees(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return GetRotationDegrees(stream);
        }
        catch
        {
            return 0;
        }
    }

    internal static int GetRotationDegrees(Stream stream)
    {
        try
        {
            if (!TryFindBox(stream, 0, stream.Length, "moov", out var moovStart, out var moovEnd)) return 0;
            if (!TryFindVideoTrack(stream, moovStart, moovEnd, out var trakStart, out var trakEnd)) return 0;
            if (!TryFindBox(stream, trakStart, trakEnd, "tkhd", out var tkhdStart, out var tkhdEnd)) return 0;
            return ReadRotationFromTkhd(stream, tkhdStart, tkhdEnd);
        }
        catch
        {
            return 0;
        }
    }

    private readonly record struct BoxHeader(string Type, long PayloadStart, long BoxEnd);

    private static bool TryReadBoxAt(Stream stream, long pos, long rangeEnd, out BoxHeader box)
    {
        box = default;
        if (pos + 8 > rangeEnd) return false;

        Span<byte> header = stackalloc byte[8];
        stream.Position = pos;
        if (stream.Read(header) != 8) return false;

        long size = BinaryPrimitives.ReadUInt32BigEndian(header);
        var type = Encoding.ASCII.GetString(header[4..8]);
        long headerSize = 8;

        if (size == 1)
        {
            // 64-bit "largesize" extension: the real size follows immediately as an 8-byte big-endian value.
            Span<byte> largeSize = stackalloc byte[8];
            if (pos + 16 > rangeEnd || stream.Read(largeSize) != 8) return false;
            size = (long)BinaryPrimitives.ReadUInt64BigEndian(largeSize);
            headerSize = 16;
        }
        else if (size == 0)
        {
            // Spec-legal shorthand for "extends to the end of the enclosing range".
            size = rangeEnd - pos;
        }

        if (size < headerSize || pos + size > rangeEnd) return false; // malformed: never trust past our own range

        box = new BoxHeader(type, pos + headerSize, pos + size);
        return true;
    }

    private static bool TryFindBox(Stream stream, long rangeStart, long rangeEnd, string fourCc, out long payloadStart, out long payloadEnd)
    {
        payloadStart = payloadEnd = 0;
        var pos = rangeStart;
        for (var i = 0; i < MaxBoxesPerLevel && pos + 8 <= rangeEnd; i++)
        {
            if (!TryReadBoxAt(stream, pos, rangeEnd, out var box)) return false;
            if (box.Type == fourCc)
            {
                payloadStart = box.PayloadStart;
                payloadEnd = box.BoxEnd;
                return true;
            }
            pos = box.BoxEnd;
        }
        return false;
    }

    // A moov can hold several "trak" boxes (video + audio + subtitle tracks): only the one with a video
    // media header ("vmhd" under trak/mdia/minf) carries the rotation that matters for display.
    private static bool TryFindVideoTrack(Stream stream, long moovStart, long moovEnd, out long trakStart, out long trakEnd)
    {
        trakStart = trakEnd = 0;
        var pos = moovStart;
        for (var i = 0; i < MaxBoxesPerLevel && pos + 8 <= moovEnd; i++)
        {
            if (!TryReadBoxAt(stream, pos, moovEnd, out var box)) return false;
            if (box.Type == "trak" && ContainsVideoMediaHeader(stream, box.PayloadStart, box.BoxEnd))
            {
                trakStart = box.PayloadStart;
                trakEnd = box.BoxEnd;
                return true;
            }
            pos = box.BoxEnd;
        }
        return false;
    }

    private static bool ContainsVideoMediaHeader(Stream stream, long trakStart, long trakEnd) =>
        TryFindBox(stream, trakStart, trakEnd, "mdia", out var mdiaStart, out var mdiaEnd) &&
        TryFindBox(stream, mdiaStart, mdiaEnd, "minf", out var minfStart, out var minfEnd) &&
        TryFindBox(stream, minfStart, minfEnd, "vmhd", out _, out _);

    // tkhd layout (ISO/IEC 14496-12): version(1) + flags(3), then creation/modification/track_ID/reserved/
    // duration sized by version (32-bit fields in v0, 64-bit in v1), then reserved(8) + layer(2) +
    // alternate_group(2) + volume(2) + reserved(2), then the 3x3 transform matrix (9 x 32-bit fixed-point).
    // Only the matrix's first two entries (a, b) are needed: for the axis-aligned 0/90/180/270 rotations
    // every camera actually writes, atan2(b, a) alone recovers the intended display angle.
    private static int ReadRotationFromTkhd(Stream stream, long payloadStart, long payloadEnd)
    {
        stream.Position = payloadStart;
        var version = stream.ReadByte();
        if (version < 0) return 0;

        long timesSize = version == 1 ? 8 + 8 + 4 + 4 + 8 : 4 + 4 + 4 + 4 + 4;
        var matrixOffset = payloadStart + 1 + 3 + timesSize + 8 + 2 + 2 + 2 + 2;

        if (matrixOffset + 8 > payloadEnd) return 0;

        Span<byte> ab = stackalloc byte[8];
        stream.Position = matrixOffset;
        if (stream.Read(ab) != 8) return 0;

        var a = BinaryPrimitives.ReadInt32BigEndian(ab[..4]) / 65536.0;
        var b = BinaryPrimitives.ReadInt32BigEndian(ab[4..8]) / 65536.0;

        var degrees = (int)Math.Round(Math.Atan2(b, a) * 180.0 / Math.PI / 90.0) * 90 % 360;
        return degrees < 0 ? degrees + 360 : degrees;
    }
}
