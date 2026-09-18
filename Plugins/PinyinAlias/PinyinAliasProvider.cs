using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.PinyinAlias;

public class PinyinAliasProvider : IAliasProvider, ITranslationProvider, IConfigurable
{
    public string Name => TranslationService.Get("Plugins_PinyinAliasPluginName");

    public string Description => TranslationService.Get("Plugin_Comp_Desc_PinyinAliasProvider");

    // 4: the alias set gained the Simplified spelling of a Traditional name. Bumping it is what makes
    // every already-built index regenerate its baked aliases -- see AliasProvidersReconciler -- which a
    // snapshot baked before this ran has none of. (Turning the setting off again does not re-bake on its
    // own: the fingerprint only tracks provider versions, so that change lands on the next rebuild.)
    public int Version => 4;

    public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(System.Reflection.Assembly.GetExecutingAssembly());

    public IReadOnlyList<(char Start, char End)> InputRanges { get; } = new[] { PinyinEngine.TableRange };

    public IReadOnlyList<(char Start, char End)> OutputRanges { get; } = new[] { ('a', 'z') };

    // Only the FULL-pinyin alias carries syllable boundaries; the initials alias is one character per
    // source character and has no internal structure (see PinyinAliasFormat). Core uses this to reject a
    // precise match that begins mid-syllable, which is what stops "ex" being read as the tail of "xue"
    // plus the head of "xi".
    public char SyllableSeparator => PinyinAliasFormat.SyllableSeparator;

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object LockObj = new();

    // Live-path result cache: FuzzyMatcher.IsMatch, HighlightMask, and PathGate's live fallback all
    // regenerate aliases for the SAME texts on every keystroke (e.g. an instant-result plugin
    // fuzzy-scanning thousands of titles per keypress). Two bounded generations, swapped when the
    // current one fills, keep memory capped with LRU-ish retention -- measured ~20x on that path.
    // Values are immutable arrays, safe to hand to any number of callers. The bulk indexing path
    // uses GetAliasesUtf8 instead and never touches this cache.
    private const int ResultCacheCap = 4096;
    private static readonly object ResultCacheLock = new();
    private static Dictionary<string, string[]> _resultCacheCur = new(StringComparer.Ordinal);
    private static Dictionary<string, string[]> _resultCachePrev = new(StringComparer.Ordinal);
    // Which flag the two generations above were baked with -- see the invalidation in GetAliases.
    private static bool _resultCacheHadConversion;

    public IReadOnlyDictionary<string, string> GetTranslations(string cultureName)
    {
        lock (LockObj)
        {
            if (Cache.TryGetValue(cultureName, out var cached))
            {
                return cached;
            }

            var translations = TranslationService.LoadEmbeddedTranslations(System.Reflection.Assembly.GetExecutingAssembly(), cultureName, "Plugin");
            Cache[cultureName] = translations;
            return translations;
        }
    }

    public bool CanHandle(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        // Vectorized range pre-gate rejects text with no char in the table's range at SIMD speed;
        // only in-range candidates pay for precise per-char table lookups.
        if (!PinyinEngine.MayContainChinese(text))
            return false;

        for (var i = 0; i < text.Length; i++)
        {
            if (PinyinEngine.IsChinese(text[i]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The Simplified spelling of <paramref name="text"/>, yielded only when conversion is switched on
    /// and the OS produced a different string. Kept pure (the converter arrives as a delegate) so the
    /// decision is testable without the native call, which is the untestable half.
    /// </summary>
    internal static IEnumerable<string> SimplifiedForms(string text, bool enabled, Func<string, string> simplify)
    {
        if (!enabled || string.IsNullOrEmpty(text))
            yield break;

        var simplified = simplify(text);
        if (!string.IsNullOrEmpty(simplified) && !string.Equals(simplified, text, StringComparison.Ordinal))
            yield return simplified;
    }
    // Read per call rather than cached here: the cached results below outlive a settings change, which
    // is exactly why the flag they were built with is remembered and the cache dropped when it flips.
    private static bool ConvertEnabled => PluginSettingsService.GetSetting(
        PinyinAliasConfigSchema.PluginId, PinyinAliasConfigSchema.ConvertSettingKey, true);

    // Drops the live-path result cache so the next call regenerates from the current setting. The cache
    // is process-wide and keyed only by text, so a test that flips the setting between calls would
    // otherwise be answered from entries baked under the previous one. The flag is left false rather
    // than read here, so the next call always re-bakes once -- one extra regeneration, not a bug.
    internal static void ResetResultCache()
    {
        lock (ResultCacheLock)
        {
            _resultCacheCur.Clear();
            _resultCachePrev.Clear();
            _resultCacheHadConversion = false;
        }
    }

    public IEnumerable<string> GetAliases(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();

        var convert = ConvertEnabled;
        lock (ResultCacheLock)
        {
            // A settings change can only reach an in-memory alias set by invalidating it: the entries
            // below were baked with the previous flag, so leaving them would keep serving an alias set
            // the user just switched off (or miss one they just switched on) for the rest of the session.
            if (convert != _resultCacheHadConversion)
            {
                _resultCacheCur.Clear();
                _resultCachePrev.Clear();
                _resultCacheHadConversion = convert;
            }

            if (_resultCacheCur.TryGetValue(text, out var cached))
                return cached;
            if (_resultCachePrev.TryGetValue(text, out cached))
            {
                _resultCacheCur[text] = cached; // promote so it survives the next swap
                return cached;
            }
        }

        var generated = PinyinAliasCombinationGenerator.GenerateAliases(text)
            .Concat(SimplifiedForms(text, convert, HanConversion.ToSimplified))
            .ToArray();

        lock (ResultCacheLock)
        {
            if (_resultCacheCur.Count >= ResultCacheCap)
            {
                (_resultCachePrev, _resultCacheCur) = (_resultCacheCur, _resultCachePrev);
                _resultCacheCur.Clear();
            }
            _resultCacheCur[text] = generated;
        }

        return generated;
    }

    // "alias" here is one single combination already (caller splits '|'-joined alternatives first).
    // Query-side counterpart of the syllable boundaries GetAliases emits: "zhengshu" is offered back
    // as "zheng<SEP>shu" so it still reaches 证书, while a term that is not a syllable sequence at all
    // ("gsh") yields nothing and is left with only the initials alias to match -- which is the whole
    // point, since that term only ever reached the full pinyin by straddling a syllable boundary.
    //
    // The Simplified spelling rides along as one more OR alternative, which is what lets a Traditional
    // query ("電腦") reach a Simplified name ("电脑") directly. It is not syllable-structured, so it is
    // treated as a flat alias by the alignment rule, exactly like the initials alias. The reverse
    // direction needs nothing here: a Simplified query is already the spelling the Traditional name's
    // alias set now carries.
    public IEnumerable<string> GetQueryForms(string term)
    {
        if (string.IsNullOrEmpty(term))
            return Array.Empty<string>();

        return PinyinQuerySegmenter.Segment(term)
            .Concat(SimplifiedForms(term, ConvertEnabled, HanConversion.ToSimplified));
    }

    public int[]? MapAliasToSourceIndices(string text, string alias)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(alias))
            return null;

        // The Simplified alias is this provider's one alias that is CJK rather than pinyin. Its positions
        // cannot be guessed from the alias length -- conversion can turn one source character into two
        // (发 -> 髮/發) or the reverse -- so each source character is converted on its own, and only when
        // every one of them maps to exactly one character is the identity mapping actually valid.
        if (ConvertEnabled && alias.Length == text.Length)
        {
            var isSimplified = true;
            for (var i = 0; i < text.Length; i++)
            {
                var converted = HanConversion.ToSimplified(text[i].ToString());
                if (converted.Length != 1 || converted[0] != alias[i])
                {
                    isSimplified = false;
                    break;
                }
            }

            if (isSimplified)
            {
                var identity = new int[text.Length];
                for (var i = 0; i < text.Length; i++)
                    identity[i] = i;
                return identity;
            }
        }

        var lists = PinyinAliasCombinationGenerator.GetSyllableLists(text);

        // Fast path: the "initials" alias contributes exactly one character per source character.
        // Verify it actually looks like initials (each alias char is the first letter of one of that
        // source character's own candidate syllables) rather than assuming from length alone --
        // coincidentally-equal lengths do happen (e.g. every character single-letter-syllable), and a
        // wrong identity mapping would silently mis-highlight rather than fail loudly.
        if (alias.Length == text.Length)
        {
            var isInitials = true;
            for (var i = 0; i < text.Length; i++)
            {
                var initial = char.ToLowerInvariant(alias[i]);
                var candidateMatches = false;
                foreach (var candidate in lists[i])
                {
                    if (candidate.Length > 0 && char.ToLowerInvariant(candidate[0]) == initial)
                    {
                        candidateMatches = true;
                        break;
                    }
                }
                if (!candidateMatches)
                {
                    isInitials = false;
                    break;
                }
            }

            if (isInitials)
            {
                var identity = new int[text.Length];
                for (var i = 0; i < text.Length; i++)
                    identity[i] = i;
                return identity;
            }
        }

        // General path: the "full pinyin" alias concatenates each character's whole syllable, which
        // can be more than one letter -- greedily walk source characters, consuming whichever
        // candidate syllable the alias actually continues with at the current position. This can
        // only mis-segment on genuinely ambiguous polyphonic overlaps; bailing out to null (no
        // highlight via this provider) is safe and no worse than today's total lack of one.
        var map = new int[alias.Length];
        var aliasPos = 0;
        for (var sourceIndex = 0; sourceIndex < text.Length && aliasPos < alias.Length; sourceIndex++)
        {
            // Consume the syllable boundary this position is generated with. It maps to the character
            // it introduces, so a query form that spans it (PinyinQuerySegmenter emits exactly that
            // shape) still highlights both syllables it sits between. Demanding it where one is due --
            // rather than skipping whatever happens to be there -- keeps this returning null for an
            // alias that did not come from this provider for this text, which is what the contract says.
            if (PinyinAliasFormat.NeedsSeparatorBefore(text, sourceIndex))
            {
                if (aliasPos >= alias.Length || alias[aliasPos] != PinyinAliasFormat.SyllableSeparator)
                    return null;
                map[aliasPos] = sourceIndex;
                aliasPos++;
            }

            var matchedLen = -1;
            foreach (var candidate in lists[sourceIndex])
            {
                if (candidate.Length > 0 && aliasPos + candidate.Length <= alias.Length &&
                    string.Compare(alias, aliasPos, candidate, 0, candidate.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    matchedLen = candidate.Length;
                    break;
                }
            }

            if (matchedLen < 0)
                return null;

            for (var j = 0; j < matchedLen; j++)
                map[aliasPos + j] = sourceIndex;
            aliasPos += matchedLen;
        }

        return aliasPos == alias.Length ? map : null;
    }

    // Byte-native mirror of GetAliases, used by the host's bulk indexing path; extracted to
    // PinyinAliasUtf8Encoder (composition, not a partial class) purely to keep this file under the
    // repo's per-file line limit.
    public void GetAliasesUtf8(string text, AliasByteSink dest) =>
        PinyinAliasUtf8Encoder.Encode(text, dest, ConvertEnabled, HanConversion.ToSimplified);

    // The one configurable surface this assembly has -- see PinyinAliasConfigSchema.
    public PluginConfigSchema GetConfigSchema() => PinyinAliasConfigSchema.Create();
}
