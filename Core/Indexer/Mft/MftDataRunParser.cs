namespace Lertaro.Core.Indexer.Mft;

// Split out of MftParser purely to keep that file under the repo's per-file line limit; this helper
// owns only non-resident $DATA run-list decoding and has no state of its own.
internal static class MftDataRunParser
{
    internal static List<(long lcn, long clusters)> ParseDataRuns(byte[] rec)
    {
        var extents = new List<(long, long)>();
        ParseDataRunsInto(rec, extents);
        return extents;
    }

    internal static void ParseDataRunsInto(byte[] rec, List<(long lcn, long clusters)> extents)
    {
        int a = BitConverter.ToUInt16(rec, 0x14);
        while (a + 8 <= rec.Length)
        {
            var type = BitConverter.ToUInt32(rec, a);
            if (type == 0xFFFFFFFF)
                break;
            var len = BitConverter.ToUInt32(rec, a + 4);
            if (len < 16 || a + len > rec.Length)
                break;
            if (type == 0x80 && rec[a + 8] == 1) // $DATA, non-resident
            {
                int mpOff = BitConverter.ToUInt16(rec, a + 0x20);
                var attributeEnd = (long)a + len;
                var p = (long)a + mpOff;
                if (p > attributeEnd || p > rec.Length)
                    break;
                long lcn = 0;
                while (p < attributeEnd && p < rec.Length && rec[(int)p] != 0)
                {
                    var hdr = rec[(int)p++];
                    var lenBytes = hdr & 0x0F;
                    var offBytes = (hdr >> 4) & 0x0F;
                    if (lenBytes == 0 || p + lenBytes > attributeEnd || p + lenBytes > rec.Length)
                        break;
                    var runLen = ReadLE(rec, (int)p, lenBytes);
                    p += lenBytes;
                    if (offBytes == 0)
                        continue; // sparse hole (unexpected for $MFT)
                    if (p + offBytes > attributeEnd || p + offBytes > rec.Length)
                        break;
                    var runOff = ReadSignedLE(rec, (int)p, offBytes);
                    p += offBytes;
                    lcn += runOff;
                    extents.Add((lcn, runLen));
                }
            }
            a += (int)len;
        }
    }

    internal static void ParseDataRunsFromAttribute(byte[] rec, int attrOffset, List<(long lcn, long clusters)> extents)
    {
        var len = BitConverter.ToUInt32(rec, attrOffset + 4);
        int mpOff = BitConverter.ToUInt16(rec, attrOffset + 0x20);
        var attributeEnd = (long)attrOffset + len;
        var p = (long)attrOffset + mpOff;
        if (p > attributeEnd || p > rec.Length)
            return;
        long lcn = 0;
        while (p < attributeEnd && p < rec.Length && rec[(int)p] != 0)
        {
            var hdr = rec[(int)p++];
            var lenBytes = hdr & 0x0F;
            var offBytes = (hdr >> 4) & 0x0F;
            if (lenBytes == 0 || p + lenBytes > attributeEnd || p + lenBytes > rec.Length)
                break;
            var runLen = ReadLE(rec, (int)p, lenBytes);
            p += lenBytes;
            if (offBytes == 0)
                continue;
            if (p + offBytes > attributeEnd || p + offBytes > rec.Length)
                break;
            var runOff = ReadSignedLE(rec, (int)p, offBytes);
            p += offBytes;
            lcn += runOff;
            extents.Add((lcn, runLen));
        }
    }

    private static long ReadLE(byte[] b, int off, int n)
    {
        long v = 0;
        for (var i = 0; i < n; i++)
            v |= (long)b[off + i] << (8 * i);
        return v;
    }

    private static long ReadSignedLE(byte[] b, int off, int n)
    {
        var v = ReadLE(b, off, n);
        if (n < 8 && (b[off + n - 1] & 0x80) != 0)
            v |= -1L << (8 * n);
        return v;
    }
}
