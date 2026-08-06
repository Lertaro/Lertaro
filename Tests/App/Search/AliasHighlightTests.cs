using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Search;

// Display highlighting for a candidate matched through an alias provider rather than its own text.
//
// It lives in the App test project because that is where Core and the plugin SDK meet: Core's own tests
// deliberately run with no alias provider registered (three of them assert behaviour that depends on
// there being none), and a plugin test project must not reference Core at all.
//
// The provider here is a fake rather than the real pinyin one, for the same reason: the logic under
// test is Core's, the pinyin plugin is not a dependency of Core, and a fake keeps the expected mask
// fixed instead of tied to the contents of a syllable table.
//
// Every case shipped wrong at least once. A highlight mask has nothing that verifies it in the search
// path, so a broken one surfaces as characters lighting up at random rather than as a failure.
[TestClass]
[DoNotParallelize] // registers into a process-wide registry and flips the process-wide fuzzy default
public sealed class AliasHighlightTests
{
    private const char Sep = (char)2;

    // Mimics the shape a transliterating provider produces: one alias of per-character initials, and
    // one of the full readings with a boundary between them.
    private sealed class FakeAliasProvider : IAliasProvider
    {
        public static readonly Dictionary<char, string> Readings = new()
        {
            ['甲'] = "jia",
            ['乙'] = "ting",
            ['丙'] = "qin",
            ['丁'] = "gan",
        };

        public string Name => "Fake";
        public IReadOnlyList<(char Start, char End)> InputRanges { get; } = new[] { ('一', '鿿') };
        public IReadOnlyList<(char Start, char End)> OutputRanges { get; } = new[] { ('a', 'z') };

        public bool CanHandle(string text) => text.Any(Readings.ContainsKey);

        public IEnumerable<string> GetAliases(string text)
        {
            var parts = text.Select(c => Readings.TryGetValue(c, out var r) ? r : c.ToString()).ToArray();
            yield return string.Concat(parts.Select(p => p[0]));
            yield return string.Join(Sep, parts);
        }

        // A real provider returns SEVERAL readings of an ambiguous term, not just the intended one --
        // the pinyin one offers up to eight. That matters here: a reading chopped into more pieces than
        // the text really has carries more boundary characters, and those are what a subsequence search
        // chases further and further right through the alias.
        public IEnumerable<string> GetQueryForms(string term)
        {
            var flat = string.Concat(Readings.Values);
            if (term.Length == 0 || !flat.StartsWith(term, StringComparison.Ordinal))
                yield break;

            var consumed = 0;
            var pieces = new List<string>();
            foreach (var r in Readings.Values)
            {
                if (consumed >= term.Length)
                    break;
                var take = Math.Min(r.Length, term.Length - consumed);
                pieces.Add(r[..take]);
                consumed += take;
            }
            if (pieces.Count > 1)
                yield return string.Join(Sep, pieces);

            // The over-split reading: same letters, one more boundary, splitting a LATER piece. Correct
            // to offer (only the candidate can settle which reading was meant) and harmless for
            // matching, but its extra boundary is exactly what a subsequence search follows deeper into
            // the alias, so it must never be treated as text the user typed.
            if (pieces.Count > 1 && pieces[^1].Length > 1)
            {
                var over = new List<string>(pieces[..^1]) { pieces[^1][..1], pieces[^1][1..] };
                yield return string.Join(Sep, over);
            }
        }

        public int[]? MapAliasToSourceIndices(string text, string alias)
        {
            var parts = text.Select(c => Readings.TryGetValue(c, out var r) ? r : c.ToString()).ToArray();
            if (alias.Length == text.Length)
                return Enumerable.Range(0, text.Length).ToArray();

            var map = new int[alias.Length];
            var pos = 0;
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    if (pos >= alias.Length || alias[pos] != Sep)
                        return null;
                    map[pos++] = i;
                }
                if (pos + parts[i].Length > alias.Length ||
                    string.CompareOrdinal(alias, pos, parts[i], 0, parts[i].Length) != 0)
                    return null;
                for (var j = 0; j < parts[i].Length; j++)
                    map[pos + j] = i;
                pos += parts[i].Length;
            }
            return pos == alias.Length ? map : null;
        }
    }

    private static bool _registered;

    [TestInitialize]
    public void Setup()
    {
        if (!_registered)
        {
            AliasProviderRegistry.Register(new FakeAliasProvider());
            _registered = true;
        }
        SearchContext.DefaultFuzzyMatchEnabled = false;
        SearchContext.DisabledAliasIds = null;
    }

    [TestCleanup]
    public void Cleanup()
    {
        SearchContext.DefaultFuzzyMatchEnabled = true;
        SearchContext.DisabledAliasIds = null;
    }

    private static int[] Lit(string text, string query)
    {
        var mask = FuzzyMatcher.ComputeHighlightMask(text, query);
        if (mask == null)
            return Array.Empty<int>();
        var lit = new List<int>();
        for (var i = 0; i < text.Length && i < mask.Length; i++)
        {
            if (mask[i])
                lit.Add(i);
        }
        return lit.ToArray();
    }

    [TestMethod]
    public void FullReadingMatch_LightsOnlyTheCharactersTyped() =>
        // "jiating" is 甲乙 and stops there. The spelling the provider supplies for matching carries
        // boundary characters and appears nowhere in the candidate, so treating it as ordinary query
        // text used to drag a subsequence across the rest of the name and light 丙丁 as well.
        CollectionAssert.AreEqual(new[] { 0, 1 }, Lit("甲乙丙丁", "jiating"));

    [TestMethod]
    public void InitialsMatch_LightsOnlyTheCharactersThoseInitialsCameFrom() =>
        // "tqg" are the initials of 乙丙丁. The same three letters also exist as a scattered
        // subsequence of the full readings (the "g" of ting, then qin's q... ) which lit characters the
        // query never described until the search stopped being allowed to scatter.
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, Lit("甲乙丙丁", "tqg"));

    [TestMethod]
    public void CrossBoundaryLetters_MatchNothingAndLightNothing()
    {
        // "gq" spans the seam between 乙 (ting) and 丙 (qin) -- letters from two different readings
        // that nobody typed as a unit.
        Assert.IsFalse(FuzzyMatcher.IsMatch("gq", "甲乙丙丁"));
        Assert.IsEmpty(Lit("甲乙丙丁", "gq"));
    }

    [TestMethod]
    public void OverSplitReading_DoesNotDragTheHighlightAcrossTheName()
    {
        // With fuzzy matching on, the mask is built by subsequence, and an over-split reading of the
        // same letters ("jia|ti|ng") can be chased right through the alias -- its trailing "g" is found
        // again inside a LATER reading, lighting a character the query never described. The spellings a
        // provider supplies exist for matching only and must never be marked as typed text.
        SearchContext.DefaultFuzzyMatchEnabled = true;

        CollectionAssert.AreEqual(new[] { 0, 1 }, Lit("甲乙丙丁", "jiating"));
    }

    [TestMethod]
    public void AnythingLit_IsBackedByAnActualMatch()
    {
        foreach (var fuzzy in new[] { false, true })
        {
            SearchContext.DefaultFuzzyMatchEnabled = fuzzy;
            foreach (var query in new[] { "jiating", "tqg" })
            {
                Assert.IsTrue(FuzzyMatcher.IsMatch(query, "甲乙丙丁"), $"fuzzy={fuzzy} {query}");
                Assert.IsNotEmpty(Lit("甲乙丙丁", query), $"fuzzy={fuzzy} {query} matched but lit nothing");
            }
        }
    }

    // "jtqin" is two initials followed by one full reading -- a subsequence of the readings alias, never
    // a contiguous run of it. So it is matchable only where the term is fuzzy, which is exactly what "'"
    // controls, and it is the shape a real pinyin query takes ("jtqinzi").
    private const string MixedShapeQuery = "jtqin";

    [TestMethod]
    public void FuzzyDisabled_AQuotedTermFlipsBackToFuzzyAndStillLights()
    {
        // Reported: with fuzzy off, "'jtqinzi" found the row and lit nothing on it. Which rule applies
        // is the TERM's, and "'" flips that term against the setting -- but highlighting was reading the
        // setting, so it went looking for a contiguous run that a fuzzy term never had to have.
        SearchContext.DefaultFuzzyMatchEnabled = false;

        Assert.IsFalse(FuzzyMatcher.IsMatch(MixedShapeQuery, "甲乙丙丁"), "unquoted, this must not match at all");
        Assert.IsTrue(FuzzyMatcher.IsMatch("'" + MixedShapeQuery, "甲乙丙丁"));
        Assert.IsNotEmpty(Lit("甲乙丙丁", "'" + MixedShapeQuery), "matched but lit nothing");
    }

    [TestMethod]
    public void FuzzyEnabled_AQuotedTermIsExactAndLightsNothingItDidNotMatch()
    {
        // The mirror: with fuzzy on, "'" makes the term exact, so the same query stops matching and
        // stops lighting. Following the setting instead of the term got this half right by accident.
        SearchContext.DefaultFuzzyMatchEnabled = true;

        Assert.IsTrue(FuzzyMatcher.IsMatch(MixedShapeQuery, "甲乙丙丁"), "unquoted and fuzzy, this matches");
        Assert.IsFalse(FuzzyMatcher.IsMatch("'" + MixedShapeQuery, "甲乙丙丁"));
        Assert.IsEmpty(Lit("甲乙丙丁", "'" + MixedShapeQuery));
    }

    // Turning a provider off has to reach the highlight too, and the way it reaches it differs by
    // process. The UI filters the provider list against the user's settings; the service cannot -- it
    // runs under an account whose LocalApplicationData is not the user's, so it reads an empty settings
    // file and considers everything enabled. What it gets is this id set, carried per request over the
    // pipe. Matching already honoured it (the ids are baked into the snapshot); generating aliases from
    // the provider directly did not, so a disabled provider still lit up characters nobody typed.
    [TestMethod]
    public void ADisabledProvidersAliasesLightNothing()
    {
        var id = AliasProviderRegistry.GetProviderId(new FakeAliasProvider());
        Assert.IsNotEmpty(Lit("甲乙丙丁", "jtqg"), "the initials alias lights up while the provider is enabled");

        SearchContext.DisabledAliasIds = new HashSet<byte> { id };

        Assert.IsEmpty(Lit("甲乙丙丁", "jtqg"), "a disabled provider must not light anything");
    }

    [TestMethod]
    public void DisablingOneProviderLeavesTheOtherTiersAlone()
    {
        // Only the alias tier is switched off. A term that matches the text itself must still light up,
        // or disabling pinyin would quietly stop highlighting ordinary names as well.
        SearchContext.DisabledAliasIds = new HashSet<byte> { AliasProviderRegistry.GetProviderId(new FakeAliasProvider()) };

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, Lit("readme.md", "readme"));
    }

    [TestMethod]
    public void ANonFuzzyTermNeverLightsAScatteredSubsequence()
    {
        // "rdm" is a subsequence of readme.md and nothing else. Where the term is not fuzzy, no such
        // match exists, so nothing may light -- the mask used to run a fuzzy position search regardless
        // of kind and lit r, d and m on a row this term had not matched at all.
        foreach (var (fuzzy, query) in new[] { (false, "rdm"), (true, "'rdm") })
        {
            SearchContext.DefaultFuzzyMatchEnabled = fuzzy;
            Assert.IsFalse(FuzzyMatcher.IsMatch(query, "readme.md"), $"fuzzy={fuzzy} {query}");
            Assert.IsEmpty(Lit("readme.md", query), $"fuzzy={fuzzy} {query} lit something it did not match");
        }
    }

    [TestMethod]
    public void ANonFuzzyTermStillReachesTheAliasTier()
    {
        // The fuzzy pass returned as soon as it found anything, so it could answer for a term whose real
        // match was through an alias. Skipping it for a non-fuzzy kind must not cost the alias its turn:
        // "jtqg" is the initials alias exactly, a contiguous run, so it matches either way.
        foreach (var (fuzzy, query) in new[] { (false, "jtqg"), (true, "'jtqg") })
        {
            SearchContext.DefaultFuzzyMatchEnabled = fuzzy;
            Assert.IsTrue(FuzzyMatcher.IsMatch(query, "甲乙丙丁"), $"fuzzy={fuzzy} {query}");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, Lit("甲乙丙丁", query), $"fuzzy={fuzzy} {query}");
        }
    }
}
