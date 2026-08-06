namespace Lertaro.Plugins.PinyinAlias.Tests;

[TestClass]
public sealed class PinyinQuerySegmenterTests
{
    private static string Sep(params string[] pieces) => string.Join(PinyinAliasFormat.SyllableSeparator, pieces);

    [TestMethod]
    public void MaxSyllableLength_CoversEverySyllableTheTableCanProduce()
    {
        // The segmenter scans at most this many characters per position, so a table syllable longer
        // than the constant would silently stop being recognisable.
        var longest = 0;
        foreach (var s in PinyinEngine.AllSyllables)
            longest = Math.Max(longest, s.Length);

        Assert.IsLessThanOrEqualTo(PinyinAliasFormat.MaxSyllableLength, longest,
            $"table has a {longest}-char syllable, constant is {PinyinAliasFormat.MaxSyllableLength}");
    }

    [TestMethod]
    public void Segment_MultiSyllableQuery_ProducesTheDelimitedForm() => CollectionAssert.Contains(PinyinQuerySegmenter.Segment("zhengshu"), Sep("zheng", "shu"));

    [TestMethod]
    public void Segment_HalfTypedLastSyllable_StillSegments() =>
        // Search-as-you-type: "zhengsh" is on the way to "zhengshu" and has to keep matching, so the
        // final piece is allowed to be a prefix of a syllable rather than a whole one.
        CollectionAssert.Contains(PinyinQuerySegmenter.Segment("zhengsh"), Sep("zheng", "sh"));

    [TestMethod]
    public void Segment_AmbiguousQuery_ReturnsBothReadings()
    {
        // "xian" is either one syllable or "xi" + "an"; only the candidate can settle which, so both
        // are offered and matched as alternatives.
        var forms = PinyinQuerySegmenter.Segment("xianggang");

        Assert.IsGreaterThan(0, forms.Length);
        CollectionAssert.Contains(forms, Sep("xiang", "gang"));
    }

    [TestMethod]
    public void Segment_SingleSyllable_IsNotSplitOnAPartialTail()
    {
        var forms = PinyinQuerySegmenter.Segment("zheng");

        // "zhen" + "g" must not appear: "g" is only the START of a syllable, and accepting it would
        // reach every 真高-like name whose next syllable merely begins with g. A query that is itself
        // a syllable only splits into whole ones.
        CollectionAssert.DoesNotContain(forms, Sep("zhen", "g"));
        // "zhe" + "ng" does appear, and legitimately so -- "ng" is a real syllable (嗯) in the table.
        CollectionAssert.Contains(forms, Sep("zhe", "ng"));
    }

    [TestMethod]
    public void Segment_SingleSyllableThatIsAlsoTwoWholeOnes_StillSplits() =>
        // The other side of the same rule: "xian" is a syllable, but "xi" + "an" are both whole
        // syllables too, so 西安 stays reachable.
        CollectionAssert.Contains(PinyinQuerySegmenter.Segment("xian"), Sep("xi", "an"));

    [TestMethod]
    public void Segment_NotASyllableSequence_ReturnsNothing()
    {
        // The actual fix: "gsh" only ever matched by straddling the 证/书 boundary in the old
        // undelimited full pinyin. It is not a syllable sequence, so it gets no delimited form and is
        // left to the initials alias, which contains no contiguous "gsh".
        Assert.IsEmpty(PinyinQuerySegmenter.Segment("gsh"));
        Assert.IsEmpty(PinyinQuerySegmenter.Segment("glyhzs"));
    }

    [TestMethod]
    public void Segment_NonLetterInput_ReturnsNothing()
    {
        Assert.IsEmpty(PinyinQuerySegmenter.Segment("readme.md"));
        Assert.IsEmpty(PinyinQuerySegmenter.Segment("01"));
        Assert.IsEmpty(PinyinQuerySegmenter.Segment(""));
    }
}
