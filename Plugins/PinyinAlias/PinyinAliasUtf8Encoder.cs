using System.Text;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.PinyinAlias;

// Extracted out of PinyinAliasProvider.cs (composition, not a partial class) purely to keep that
// file under the repo's per-file line limit. This is the byte-native mirror of the string-path
// alias generation there: used by the host's bulk indexing path to assemble aliases directly from
// pre-encoded syllable bytes into the sink, never materializing a string. Verified byte-identical
// (decoded) to the string path across 200k-name equivalence runs plus adversarial corpora before
// adoption -- keep the two paths' combination logic (32-combination cap, steps budget, dedup order)
// in lockstep if either changes.
internal static class PinyinAliasUtf8Encoder
{
    [ThreadStatic] private static ushort[]?[]? _idScratch;
    [ThreadStatic] private static AliasByteSink? _fullCombosScratch;
    [ThreadStatic] private static AliasByteSink? _initialCombosScratch;
    [ThreadStatic] private static byte[]? _comboFullBytesScratch;
    [ThreadStatic] private static char[]? _comboInitialCharsScratch;

    public static void Encode(string text, AliasByteSink dest)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (text.Length == 1)
        {
            if (PinyinEngine.TryGetPinyinIds(text[0], out var soloIds))
            {
                foreach (var id in soloIds)
                {
                    var start = dest.BeginSegment();
                    dest.Append(PinyinEngine.GetSyllableUtf8(id));
                    dest.EndSegment(start);
                }
            }
            return;
        }

        var ids = _idScratch;
        if (ids == null || ids.Length < text.Length)
            _idScratch = ids = new ushort[]?[Math.Max(text.Length, 64)];

        // Fill EVERY position first, THEN count: breaking out of a fused fill+count loop early
        // would leave stale entries from a previous call in the thread-static scratch beyond the
        // break point.
        for (var i = 0; i < text.Length; i++)
            ids[i] = PinyinEngine.TryGetPinyinIds(text[i], out var charIds) ? charIds : null;

        var totalCombinations = 1L;
        for (var i = 0; i < text.Length; i++)
        {
            totalCombinations *= ids[i]?.Length ?? 1;
            if (totalCombinations > 32)
                break;
        }

        if (totalCombinations == 1)
        {
            var initialsStart = dest.BeginSegment();
            for (var i = 0; i < text.Length; i++)
                AppendInitial(dest, text, i, ids[i]);
            dest.EndSegment(initialsStart);

            var fullStart = dest.BeginSegment();
            for (var i = 0; i < text.Length; i++)
                AppendFull(dest, text, i, ids[i]);

            // Same "full == initials -> yield once" rule as the string path.
            if (dest.Pending(fullStart).SequenceEqual(dest.Segment(dest.SegmentCount - 1)))
                dest.AbandonSegment(fullStart);
            else
                dest.EndSegment(fullStart);
            return;
        }

        // Combination (polyphonic) path -- byte-native mirror of GenerateCombinations, same steps
        // budget, same fixed 256-byte full-pinyin cap.
        var fulls = _fullCombosScratch ??= new AliasByteSink();
        var initials = _initialCombosScratch ??= new AliasByteSink();
        fulls.Reset();
        initials.Reset();

        var fullBuffer = _comboFullBytesScratch ??= new byte[256];
        var initialBuffer = _comboInitialCharsScratch;
        if (initialBuffer == null || initialBuffer.Length < text.Length)
            _comboInitialCharsScratch = initialBuffer = new char[Math.Max(text.Length, 64)];

        var count = 0;
        var steps = 0;
        RecurseBytes(text, ids, 0, 0, fulls, initials, fullBuffer, initialBuffer, ref count, ref steps);

        var initialsGroupStart = dest.BeginSegment();
        JoinUniqueSegments(initials, dest);
        var hadInitials = dest.Pending(initialsGroupStart).Length > 0;
        dest.EndSegment(initialsGroupStart);

        var fullsGroupStart = dest.BeginSegment();
        JoinUniqueSegments(fulls, dest);
        if (dest.Pending(fullsGroupStart).Length == 0)
        {
            dest.AbandonSegment(fullsGroupStart);
        }
        else if (hadInitials && dest.Pending(fullsGroupStart).SequenceEqual(dest.Segment(dest.SegmentCount - 1)))
        {
            dest.AbandonSegment(fullsGroupStart);
        }
        else
        {
            dest.EndSegment(fullsGroupStart);
        }
    }

    private static void AppendInitial(AliasByteSink dest, string text, int i, ushort[]? charIds)
    {
        if (charIds != null)
            dest.Append(PinyinEngine.GetSyllableUtf8(charIds[0])[0]);
        else
            AppendLiteralChar(dest, text[i], i > 0 ? text[i - 1] : '\0', i + 1 < text.Length ? text[i + 1] : '\0');
    }

    private static void AppendFull(AliasByteSink dest, string text, int i, ushort[]? charIds)
    {
        if (PinyinAliasFormat.NeedsSeparatorBefore(text, i))
            dest.Append((byte)PinyinAliasFormat.SyllableSeparator);
        if (charIds != null)
            dest.Append(PinyinEngine.GetSyllableUtf8(charIds[0]));
        else
            AppendLiteralChar(dest, text[i], i > 0 ? text[i - 1] : '\0', i + 1 < text.Length ? text[i + 1] : '\0');
    }

    // Encodes one literal (non-CJK) source position. Every position emits exactly one alias element
    // in order, so a surrogate pair's halves always land adjacent -- the string path re-pairs them
    // inside the alias string for free, and here the pair must be encoded together (a UTF-16 half
    // encoded alone is invalid UTF-8 and turns into U+FFFD, corrupting emoji/CJK-extension chars).
    // The HIGH half emits the whole pair's bytes; the matching LOW half then emits nothing.
    private static void AppendLiteralChar(AliasByteSink dest, char c, char prev, char next)
    {
        if (char.IsHighSurrogate(c) && char.IsLowSurrogate(next))
        {
            Span<byte> tmp = stackalloc byte[4];
            var written = new Rune(c, next).EncodeToUtf8(tmp);
            dest.Append(tmp[..written]);
            return;
        }
        if (char.IsLowSurrogate(c) && char.IsHighSurrogate(prev))
            return;

        var lower = char.ToLowerInvariant(c);
        if (lower < 128)
            dest.Append((byte)lower);
        else
            AppendUtf8Char(dest, lower);
    }

    private static void AppendUtf8Char(AliasByteSink dest, char c)
    {
        Span<byte> tmp = stackalloc byte[4];
        Span<char> one = stackalloc char[1];
        one[0] = c;
        var written = Encoding.UTF8.GetBytes(one, tmp);
        dest.Append(tmp[..written]);
    }

    private static void RecurseBytes(
        string text,
        ushort[]?[] ids,
        int index,
        int fullLen,
        AliasByteSink fulls,
        AliasByteSink initials,
        byte[] fullBuffer,
        char[] initialBuffer,
        ref int count,
        ref int steps)
    {
        // Same steps budget as GenerateCombinations -- see the comment there.
        if (++steps > text.Length * 32 + 256) return;
        if (count >= 32) return;

        if (index == text.Length)
        {
            var fs = fulls.BeginSegment();
            fulls.Append(fullBuffer.AsSpan(0, fullLen));
            fulls.EndSegment(fs);

            var istart = initials.BeginSegment();
            for (var i = 0; i < text.Length; i++)
            {
                var c = initialBuffer[i];
                if (c < 128) initials.Append((byte)c);
                else AppendLiteralChar(initials, c, i > 0 ? initialBuffer[i - 1] : '\0', i + 1 < text.Length ? initialBuffer[i + 1] : '\0');
            }
            initials.EndSegment(istart);
            count++;
            return;
        }

        var charIds = ids[index];
        if (charIds == null)
        {
            var c = text[index];
            int written;
            if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                Span<byte> tmp = stackalloc byte[4];
                written = new Rune(c, text[index + 1]).EncodeToUtf8(tmp);
                if (fullLen + written > fullBuffer.Length) return;
                tmp[..written].CopyTo(fullBuffer.AsSpan(fullLen));
                initialBuffer[index] = c;
            }
            else if (char.IsLowSurrogate(c) && index > 0 && char.IsHighSurrogate(text[index - 1]))
            {
                written = 0;
                initialBuffer[index] = c;
            }
            else
            {
                var lower = char.ToLowerInvariant(c);
                if (lower < 128)
                {
                    if (fullLen + 1 > fullBuffer.Length) return;
                    fullBuffer[fullLen] = (byte)lower;
                    written = 1;
                }
                else
                {
                    Span<byte> tmp = stackalloc byte[4];
                    Span<char> one = stackalloc char[1];
                    one[0] = lower;
                    written = Encoding.UTF8.GetBytes(one, tmp);
                    if (fullLen + written > fullBuffer.Length) return;
                    tmp[..written].CopyTo(fullBuffer.AsSpan(fullLen));
                }
                initialBuffer[index] = lower;
            }
            RecurseBytes(text, ids, index + 1, fullLen + written, fulls, initials, fullBuffer, initialBuffer, ref count, ref steps);
            return;
        }

        // Mirrors GenerateCombinations: the boundary is written before the syllable, inside the loop,
        // so every branch of the polyphonic expansion carries it.
        var separate = PinyinAliasFormat.NeedsSeparatorBefore(text, index);
        foreach (var id in charIds)
        {
            var at = fullLen;
            if (separate)
            {
                if (at + 1 > fullBuffer.Length)
                    continue;
                fullBuffer[at++] = (byte)PinyinAliasFormat.SyllableSeparator;
            }

            var syl = PinyinEngine.GetSyllableUtf8(id);
            if (at + syl.Length > fullBuffer.Length)
                continue;
            syl.CopyTo(fullBuffer.AsSpan(at));
            initialBuffer[index] = (char)syl[0];
            RecurseBytes(text, ids, index + 1, at + syl.Length, fulls, initials, fullBuffer, initialBuffer, ref count, ref steps);
        }
    }

    // Appends the unique segments of `source` (first-seen order, matching the string path's
    // List.Contains dedup) joined by '|' into the currently-open segment of `dest`.
    private static void JoinUniqueSegments(AliasByteSink source, AliasByteSink dest)
    {
        var wroteAny = false;
        for (var i = 0; i < source.SegmentCount; i++)
        {
            var seg = source.Segment(i);
            if (seg.IsEmpty)
                continue;

            var duplicate = false;
            for (var j = 0; j < i; j++)
            {
                if (source.Segment(j).SequenceEqual(seg))
                {
                    duplicate = true;
                    break;
                }
            }
            if (duplicate)
                continue;

            if (wroteAny)
                dest.Append((byte)'|');
            dest.Append(seg);
            wroteAny = true;
        }
    }
}
