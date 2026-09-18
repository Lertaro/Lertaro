using System.Runtime.InteropServices;

namespace Lertaro.Plugins.PinyinAlias;

// Makes a Simplified query reach a Traditional-named file, and vice versa, by asking Windows for the
// Simplified spelling of a name (the alias side) or of a typed query (the query side). The system
// implementation is used instead of a bundled Traditional/Simplified table or a third-party library
// because it ships with the OS, needs no data file, and covers every character the pinyin table spans
// -- see TableRange in PinyinEngine.
//
// ponytail: this is the OS's locale CHARACTER/WORD mapping, not phrase-level disambiguation like OpenCC.
// A handful of context-sensitive characters (the 著/着 and 乾/干 families) can therefore convert less
// well than a dedicated phrase table would, because the correct reading depends on the surrounding word
// rather than the character alone. It is still strictly better than no conversion at all, and the
// upgrade path is to replace the body of ToSimplified with such a table if the difference ever matters.
internal static class HanConversion
{
    private const uint LcmapSimplifiedChinese = 0x02000000;
    private const string LocaleName = "zh-CN";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int LCMapStringEx(
        string lpLocaleName,
        uint dwMapFlags,
        char[] lpSrcStr,
        int cchSrc,
        char[]? lpDestStr,
        int cchDest,
        nint lpVersionInformation,
        nint lpReserved,
        nint lpSortHandle);

    // Bounded the way this repo's other result caches are: the bake path calls this once per unique name
    // and the query path once per query, so it is never a hot path -- but a rebuild walks tens of
    // thousands of names and some of them repeat, while the cap keeps a long-running process's cache
    // from growing without limit. A full clear on overflow is deliberate: nothing here needs ordering
    // preserved, only a cheap bound.
    private const int MaxCacheEntries = 4096;
    private static readonly Dictionary<string, string> Cache = new(StringComparer.Ordinal);
    private static readonly object CacheLock = new();

    // The mapping is stable for the process lifetime, so one probe (in IsConversionAvailable) settles it.
    private static int _available = -1;

    /// <summary>
    /// Whether the native conversion is usable in this process. Probed once and cached; false on a host
    /// where the API is unavailable or refuses the mapping.
    /// </summary>
    internal static bool IsConversionAvailable()
    {
        var known = Volatile.Read(ref _available);
        if (known >= 0)
            return known == 1;

        var available = Map("中").Length > 0;
        Volatile.Write(ref _available, available ? 1 : 0);
        return available;
    }

    /// <summary>
    /// The Simplified Chinese spelling of <paramref name="text"/>, or <paramref name="text"/> itself when
    /// there is nothing to convert or the OS call fails -- never null, never an exception.
    /// </summary>
    internal static string ToSimplified(string text)
    {
        if (string.IsNullOrEmpty(text) || !HasCjk(text))
            return text;

        lock (CacheLock)
        {
            if (Cache.TryGetValue(text, out var cached))
                return cached;
        }

        var mapped = Map(text);
        var result = mapped.Length == 0 ? text : mapped;

        lock (CacheLock)
        {
            if (Cache.Count >= MaxCacheEntries)
                Cache.Clear();
            Cache[text] = result;
        }

        return result;
    }

    // The OS call. A zero-length destination is the documented way to ask how long the mapped string is,
    // so the real call never has to guess a size and grow it; the second call can therefore only fail
    // outright -- which is reported as an empty string and treated by the caller as "nothing to convert".
    private static string Map(string text)
    {
        var source = text.ToCharArray();
        var required = LCMapStringEx(LocaleName, LcmapSimplifiedChinese, source, source.Length, null, 0, 0, 0, 0);
        if (required <= 0)
            return string.Empty;

        var buffer = new char[required];
        var written = LCMapStringEx(LocaleName, LcmapSimplifiedChinese, source, source.Length, buffer, buffer.Length, 0, 0, 0);
        if (written <= 0 || written > buffer.Length)
            return string.Empty;

        return new string(buffer, 0, written);
    }

    // Cheap gate before touching the OS: text with no CJK ideograph in the table's own range cannot
    // contain anything the mapping would change, so the call is skipped entirely for ASCII/Latin text.
    private static bool HasCjk(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c >= PinyinEngine.TableRange.Start && c <= PinyinEngine.TableRange.End)
                return true;
        }
        return false;
    }
}
