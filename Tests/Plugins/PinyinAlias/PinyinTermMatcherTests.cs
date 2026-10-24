namespace Lertaro.Plugins.PinyinAlias.Tests;

// The NFA's own semantics, ported from WinShun (pinyin::Matcher): any non-empty prefix of any reading
// may be consumed by a character, the matched span must be contiguous, 'v' accepts 'u', and a word is
// 2..63 ASCII letters/digits with at least one letter. Common-only falls back to the frequency-ordered
// table's two most frequent readings.
[TestClass]
public sealed class PinyinTermMatcherTests
{
    private static bool Find(string term, string text, bool commonOnly, out int start, out int end)
    {
        var matcher = new PinyinTermMatcher(term, commonOnly);
        start = end = -1;
        if (!matcher.IsValid)
            return false;
        return matcher.TryFind(text, default, out start, out end);
    }

    [TestMethod]
    [DataRow("bg", "报告")]
    [DataRow("baogao", "报告")]
    [DataRow("baog", "报告")]
    [DataRow("bgao", "报告")]
    [DataRow("bga", "报告")] // 告 gives up its half-typed "ga" -- allowed anywhere in the term
    [DataRow("bg", "x报告")] // the run may start anywhere
    public void Spellings_ReachTheName(string term, string text)
        => Assert.IsTrue(Find(term, text, false, out _, out _), $"'{term}' must find '{text}'");

    [TestMethod]
    public void Match_ReportsTheLeftmostSpan()
    {
        Assert.IsTrue(Find("bg", "x报告", false, out var start, out var end));
        Assert.AreEqual(1, start);
        Assert.AreEqual(3, end);
    }

    [TestMethod]
    public void ReadingPrefix_MustOpenTheWord()
    {
        // su/suan are the two permitted shapes of 算; "uan" opens inside the reading.
        Assert.IsTrue(Find("su", "算", false, out _, out _));
        Assert.IsTrue(Find("suan", "算", false, out _, out _));
        Assert.IsFalse(Find("uan", "算", false, out _, out _));
    }

    [TestMethod]
    [DataRow("s")] // a single letter is not one of this provider's words at all
    [DataRow("12")] // digits only
    [DataRow("a-b")]
    [DataRow("")]
    public void WordShape_IsRejected(string term)
        => Assert.IsFalse(new PinyinTermMatcher(term, false).IsValid);

    [TestMethod]
    public void WordShape_Bounds()
    {
        Assert.IsTrue(new PinyinTermMatcher(new string('a', 63), false).IsValid);
        Assert.IsFalse(new PinyinTermMatcher(new string('a', 64), false).IsValid);
        Assert.IsTrue(new PinyinTermMatcher("a1", false).IsValid);
    }

    [TestMethod]
    public void CharactersInsideTheSpan_MustConsumeSomething()
    {
        // 笔abc记: "bj" would have to skip a/b/c, which this matcher never does...
        Assert.IsFalse(Find("bj", "笔abc记", false, out _, out _));
        // ...while every character paying one letter is exactly the contiguous walk.
        Assert.IsTrue(Find("babcj", "笔abc记", false, out _, out _));
    }

    [TestMethod]
    public void NonMatchingCharacter_BreaksTheWalk()
    {
        // '-' can neither match a letter nor be skipped.
        Assert.IsFalse(Find("bg", "报-告", false, out _, out _));
    }

    [TestMethod]
    public void Umlaut_AcceptsU()
    {
        Assert.IsTrue(Find("lv", "绿", false, out _, out _));
        Assert.IsTrue(Find("lu", "绿", false, out _, out _));
        Assert.IsTrue(Find("nv", "女", false, out _, out _));
        Assert.IsTrue(Find("nu", "女", false, out _, out _));
    }

    [TestMethod]
    public void AsciiDigits_PassThroughLiterally()
    {
        Assert.IsTrue(Find("di01ji", "第01集", false, out _, out _));
    }

    [TestMethod]
    public void CommonOnly_AcceptsTheTwoMostFrequentReadings()
    {
        // 重 = zhong, chong, tong, most common first (the regenerated table's order).
        Assert.IsTrue(Find("zhong", "重", true, out _, out _));
        Assert.IsTrue(Find("chong", "重", true, out _, out _));
        Assert.IsFalse(Find("tong", "重", true, out _, out _));
    }

    [TestMethod]
    public void CommonOnly_AcceptsBothReadingsRegardlessOfWord()
    {
        // No word dictionary: the second reading is common everywhere, not only in 重复/银行/音乐.
        Assert.IsTrue(Find("zhong", "重复", true, out _, out _));
        Assert.IsTrue(Find("chong", "重复", true, out _, out _));
        Assert.IsTrue(Find("xing", "银行", true, out _, out _));
        Assert.IsTrue(Find("hang", "银行", true, out _, out _));
        Assert.IsTrue(Find("le", "音乐", true, out _, out _));
        Assert.IsTrue(Find("yue", "音乐", true, out _, out _));
    }

    [TestMethod]
    public void Painted_CoversTheConsumedSpan()
    {
        var matcher = new PinyinTermMatcher("bg", false);
        var painted = new bool[3];
        Assert.IsTrue(matcher.TryFind("x报告", painted, out var start, out var end));
        Assert.AreEqual(1, start);
        Assert.AreEqual(3, end);
        CollectionAssert.AreEqual(new[] { false, true, true }, painted);
    }
}
