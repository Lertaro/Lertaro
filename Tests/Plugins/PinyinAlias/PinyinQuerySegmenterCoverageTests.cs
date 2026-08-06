namespace Lertaro.Plugins.PinyinAlias.Tests;

// Measures the property the segmenter exists for: given the concatenated pinyin of a real character
// sequence, does it offer back the reading that actually produced it? A miss is a search that silently
// finds nothing, so this is a threshold test rather than an example-based one.
[TestClass]
public sealed class PinyinQuerySegmenterCoverageTests
{
    private static List<char> PronounceableChars()
    {
        var chars = new List<char>();
        for (var c = PinyinEngine.TableRange.Start; c < PinyinEngine.TableRange.End; c++)
        {
            if (PinyinEngine.TryGetPinyins(c, out var p) && p.Length > 0 && p[0].Length > 0)
                chars.Add(c);
        }
        return chars;
    }

    // Fixed seed: this has to fail reproducibly, not intermittently.
    private static (int Total, int Hit) Measure(int syllables, int samples, int seed, bool dropLastLetter)
    {
        var chars = PronounceableChars();
        var rnd = new Random(seed);
        int total = 0, hit = 0;

        for (var n = 0; n < samples; n++)
        {
            var pieces = new string[syllables];
            for (var i = 0; i < syllables; i++)
            {
                PinyinEngine.TryGetPinyins(chars[rnd.Next(chars.Count)], out var p);
                pieces[i] = p[0];
            }

            if (dropLastLetter)
            {
                if (pieces[^1].Length < 2)
                    continue;
                pieces[^1] = pieces[^1][..^1];
            }

            var query = string.Concat(pieces);
            if (query.Length > 32)
                continue;

            total++;
            if (PinyinQuerySegmenter.Segment(query).Contains(string.Join(PinyinAliasFormat.SyllableSeparator, pieces)))
                hit++;
        }
        return (total, hit);
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void Segment_TypicalQueryLengths_AlwaysOfferTheProducingReading(int syllables)
    {
        // Two to four syllables covers essentially every query a person types at a search box, and the
        // producing reading is expected every single time at that length.
        var (total, hit) = Measure(syllables, 2000, 4000 + syllables, dropLastLetter: false);

        Assert.IsGreaterThan(0, total);
        Assert.AreEqual(total, hit, $"{syllables} syllables: {total - hit} of {total} lost the producing reading");
    }

    [TestMethod]
    public void Segment_HalfTypedQueries_KeepTheProducingReading()
    {
        // Search-as-you-type spends most of its time here, one letter short of a whole syllable. The
        // residual miss is a long query whose fewest-piece readings tie beyond the per-query form cap.
        var (total, hit) = Measure(3, 2000, 7001, dropLastLetter: true);

        Assert.IsGreaterThan(0, total);
        Assert.IsGreaterThan(0.98, (double)hit / total, $"half-typed: only {hit} of {total} kept the reading");
    }

    [TestMethod]
    public void Segment_LongQueries_StayWellCoveredAndBounded()
    {
        // Far past realistic input, included so a future change that trades long-query coverage away
        // has to do it visibly.
        var (total, hit) = Measure(8, 800, 9001, dropLastLetter: false);

        Assert.IsGreaterThan(0, total);
        Assert.IsGreaterThan(0.99, (double)hit / total, $"8 syllables: only {hit} of {total}");
    }

    [TestMethod]
    public void Segment_PathologicalInput_StaysCheap()
    {
        // Every position ambiguous. The step budget, not the syllable table, is what bounds this.
        // Warm first: the syllable/prefix sets are built in a static initializer and the first call
        // also pays JIT, neither of which is the steady-state cost this guards.
        for (var i = 0; i < 20; i++)
            PinyinQuerySegmenter.Segment(new string('a', 32));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 200; i++)
            PinyinQuerySegmenter.Segment(new string('a', 32));
        sw.Stop();

        // Deliberately loose. What this catches is the combinatorial blow-up the step budget exists to
        // prevent, which costs milliseconds or worse -- not a handful of microseconds either way, and
        // a tight bound here would only measure how far tiered JIT happened to get in a short test run.
        //
        // Loose enough to survive a busy machine, too. At 500 this failed intermittently while the whole
        // test solution ran, which is nineteen assemblies competing for cores: a wall-clock bound tight
        // enough to be crossed by scheduling pressure alone reports load as a regression. The blow-up
        // this guards against is orders of magnitude away from either number, so the headroom costs the
        // check nothing.
        Assert.IsLessThan(20_000, sw.Elapsed.TotalMilliseconds * 1000 / 200, "per-query microseconds");
    }
}
