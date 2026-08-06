namespace Lertaro.Plugins.PinyinAlias;

public static class PinyinEngine
{
    // The compact pinyin lookup table (syllable strings + a per-Unicode-codepoint index into them) --
    // was previously a ~3900-line byte[] array literal inline in this file; moved to an embedded
    // resource purely so this source file stays readable, with zero behavior or performance change
    // (the compiler already turned a constant byte[] literal into a single block-copy via
    // RuntimeHelpers.InitializeArray, same as this does at class-init time; matched by suffix rather
    // than a hardcoded full resource name, mirroring TranslationService.LoadEmbeddedTranslations).
    private static readonly byte[] RawData = LoadRawData();

    private static byte[] LoadRawData()
    {
        var assembly = typeof(PinyinEngine).Assembly;
        string? matched = null;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (name.EndsWith("pinyin.bin", StringComparison.OrdinalIgnoreCase))
            {
                matched = name;
                break;
            }
        }
        if (matched == null)
            throw new InvalidOperationException("Embedded pinyin data resource (pinyin.bin) not found.");

        using var stream = assembly.GetManifestResourceStream(matched)!;
        var data = new byte[stream.Length];
        var offset = 0;
        int read;
        while (offset < data.Length && (read = stream.Read(data, offset, data.Length - offset)) > 0)
            offset += read;
        return data;
    }

    private static readonly string[] Syllables;
    private static readonly int MultiTableOffset;
    private static readonly int CharTableOffset;

    // Pre-built one-element result arrays per syllable id: the (dominant) single-pronunciation
    // lookup used to allocate a fresh string[1] on EVERY call, which at snapshot-bake volume is
    // millions of allocations per rebuild for identical contents. A few hundred tiny arrays built
    // once cover it. Callers must treat returned arrays as read-only (they are shared).
    private static readonly string[][] SingleSyllableArrays;
    // Same idea for polyphonic entries, keyed by the multi-table offset, built lazily on first
    // lookup. Concurrent first lookups may build the same array twice; the reference store is
    // atomic and both copies are identical, so last-write-wins is benign.
    private static readonly string[]?[] MultiSyllableArrays;

    // Byte-native mirrors for GetAliasesUtf8: syllables pre-encoded as (pure-ASCII) UTF-8, and
    // id-array lookups so aliases can be assembled without touching syllable strings at all.
    private static readonly byte[][] SyllableUtf8;
    private static readonly ushort[][] SingleIdArrays;
    private static readonly ushort[]?[] MultiIdArrays;

    static PinyinEngine()
    {
        var numSyllables = ReadUInt16(0);
        MultiTableOffset = ReadInt32(2);
        CharTableOffset = ReadInt32(6);

        Syllables = new string[numSyllables];
        var offset = 16;
        for (var i = 0; i < numSyllables; i++)
        {
            var len = RawData[offset++];
            Syllables[i] = System.Text.Encoding.UTF8.GetString(RawData, offset, len);
            offset += len;
        }

        SingleSyllableArrays = new string[numSyllables][];
        SyllableUtf8 = new byte[numSyllables][];
        SingleIdArrays = new ushort[numSyllables][];
        for (var i = 0; i < numSyllables; i++)
        {
            SingleSyllableArrays[i] = new[] { Syllables[i] };
            SyllableUtf8[i] = System.Text.Encoding.ASCII.GetBytes(Syllables[i]);
            SingleIdArrays[i] = new[] { (ushort)i };
        }

        // Multi-table entries live between MultiTableOffset and CharTableOffset; offsets into the
        // table are in 16-bit units, so half the byte span bounds the distinct key space.
        MultiSyllableArrays = new string[]?[(CharTableOffset - MultiTableOffset) / 2];
        MultiIdArrays = new ushort[]?[(CharTableOffset - MultiTableOffset) / 2];
    }

    private static ushort ReadUInt16(int offset) => (ushort)(RawData[offset] | (RawData[offset + 1] << 8));

    private static short ReadInt16(int offset) => (short)(RawData[offset] | (RawData[offset + 1] << 8));

    private static int ReadInt32(int offset) => RawData[offset] |
               (RawData[offset + 1] << 8) |
               (RawData[offset + 2] << 16) |
               (RawData[offset + 3] << 24);

    public static bool IsChinese(char c)
    {
        var index = c - 12295;
        if (index < 0 || index >= 28647)
        {
            return false;
        }
        return ReadUInt16(CharTableOffset + index * 2) != 0xFFFF;
    }

    public static bool TryGetPinyins(char c, out string[] pinyins)
    {
        var index = c - 12295;
        if (index < 0 || index >= 28647)
        {
            pinyins = Array.Empty<string>();
            return false;
        }

        var val = ReadUInt16(CharTableOffset + index * 2);
        if (val == 0xFFFF)
        {
            pinyins = Array.Empty<string>();
            return false;
        }

        if (val < 0x8000)
        {
            pinyins = SingleSyllableArrays[val];
            return true;
        }

        var offset = val - 0x8000;
        var cached = MultiSyllableArrays[offset];
        if (cached != null)
        {
            pinyins = cached;
            return true;
        }

        var byteOffset = MultiTableOffset + offset * 2;
        int len = ReadUInt16(byteOffset);
        var built = new string[len];
        for (var i = 0; i < len; i++)
        {
            var syllableIdx = ReadUInt16(byteOffset + 2 + i * 2);
            built[i] = Syllables[syllableIdx];
        }
        MultiSyllableArrays[offset] = built;
        pinyins = built;
        return true;
    }

    /// <summary>
    /// Id-array counterpart of <see cref="TryGetPinyins"/> for byte-native alias assembly: returns
    /// the syllable ids for <paramref name="c"/>, resolvable to UTF-8 via <see cref="GetSyllableUtf8"/>.
    /// Returned arrays are shared and must be treated as read-only.
    /// </summary>
    public static bool TryGetPinyinIds(char c, out ushort[] ids)
    {
        var index = c - 12295;
        if (index < 0 || index >= 28647)
        {
            ids = Array.Empty<ushort>();
            return false;
        }

        var val = ReadUInt16(CharTableOffset + index * 2);
        if (val == 0xFFFF)
        {
            ids = Array.Empty<ushort>();
            return false;
        }

        if (val < 0x8000)
        {
            ids = SingleIdArrays[val];
            return true;
        }

        var offset = val - 0x8000;
        var cached = MultiIdArrays[offset];
        if (cached != null)
        {
            ids = cached;
            return true;
        }

        var byteOffset = MultiTableOffset + offset * 2;
        int len = ReadUInt16(byteOffset);
        var built = new ushort[len];
        for (var i = 0; i < len; i++)
            built[i] = ReadUInt16(byteOffset + 2 + i * 2);
        MultiIdArrays[offset] = built;
        ids = built;
        return true;
    }

    /// <summary>The (pure-ASCII) UTF-8 bytes of syllable <paramref name="id"/>. Shared; read-only.</summary>
    public static byte[] GetSyllableUtf8(int id) => SyllableUtf8[id];

    /// <summary>
    /// Every syllable this table can produce. Read-only; shared. Lets the query side recognise which
    /// letter runs are whole syllables, which is what makes a typed query expressible in the same
    /// syllable-delimited shape the generated aliases use.
    /// </summary>
    public static IReadOnlyList<string> AllSyllables => Syllables;

    /// <summary>
    /// Vectorized pre-gate: the char table covers exactly [12295, 12295+28647), so a string with no
    /// char in that range can be rejected by the SIMD-backed BCL range scan without per-char table
    /// lookups. A true result still needs the precise <see cref="IsChinese"/> check per char.
    /// </summary>
    public static bool MayContainChinese(ReadOnlySpan<char> text)
        => text.ContainsAnyInRange((char)12295, (char)(12295 + 28647 - 1));

    /// <summary>The char table's covered range, as (first char, last char) -- mirrors the bounds baked into <see cref="MayContainChinese"/>/<see cref="IsChinese"/>.</summary>
    public static readonly (char Start, char End) TableRange = ((char)12295, (char)(12295 + 28647 - 1));
}
