using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

// Only the backtracking fallback carries a match budget: NonBacktracking cannot time out at all, and it
// is tried first. So a timeout means a user's lookaround/backreference pattern lost a race against one
// name -- which must cost that one candidate and a throttled log line, never the whole search.
[TestClass]
public sealed class RegexTimeoutTests
{
    [TestMethod]
    public void Throttle_LogsTheFirstTimeoutThenSuppressesRepeats()
    {
        var throttle = new RegexTimeoutLogThrottle(60_000);

        Assert.IsTrue(throttle.ShouldLog("p", 1_000));
        Assert.IsFalse(throttle.ShouldLog("p", 1_001));
        Assert.IsFalse(throttle.ShouldLog("p", 60_999));
        Assert.IsTrue(throttle.ShouldLog("p", 61_000));
    }

    [TestMethod]
    public void Throttle_TracksEachPatternSeparately()
    {
        var throttle = new RegexTimeoutLogThrottle(60_000);

        Assert.IsTrue(throttle.ShouldLog("a", 1_000));
        Assert.IsTrue(throttle.ShouldLog("b", 1_000));
        Assert.IsFalse(throttle.ShouldLog("a", 1_001));
        Assert.IsFalse(throttle.ShouldLog("b", 1_001));
    }

    [TestMethod]
    public void Throttle_TrackedPatterns_AreBounded()
    {
        // Every intermediate state of an edit is a distinct pattern, so the map is unbounded input: it is
        // cleared wholesale at a cap, the same trade the compiled-regex cache beside it makes.
        var throttle = new RegexTimeoutLogThrottle(60_000);

        for (var i = 0; i < 1_000; i++)
            throttle.ShouldLog($"pattern-{i}", 1_000 + i);

        Assert.IsLessThanOrEqualTo(256, throttle.TrackedCount);
    }

    [TestMethod]
    public void Throttle_PatternSurvivesUntilTheCapIsReached()
    {
        var throttle = new RegexTimeoutLogThrottle(60_000);

        Assert.IsTrue(throttle.ShouldLog("keep", 1_000));
        for (var i = 0; i < 100; i++)
            throttle.ShouldLog($"other-{i}", 1_000);

        // Below the cap nothing is forgotten, so the repeat is still suppressed.
        Assert.IsFalse(throttle.ShouldLog("keep", 1_001));
    }

    [TestMethod]
    public void AllMatch_PathologicalPattern_TreatsTheCandidateAsAMissInsteadOfThrowing()
    {
        // The lookahead is unsupported under NonBacktracking, so this pattern is compiled by the timed
        // fallback; "(a+)+$" against a long non-matching tail is the classic shape that exhausts its
        // budget. The candidate must simply be rejected.
        var patterns = new[] { @"(?=(a+)+$)" };

        var start = Environment.TickCount64;
        var matched = RegexClauses.AllMatch(patterns, new string('a', 30) + "!");
        var elapsed = Environment.TickCount64 - start;

        Assert.IsFalse(matched);

        // Finishing instantly would mean the pattern never reached the timed fallback at all, in which
        // case this test proved nothing. The budget is 250ms and no machine finishes 2^29 backtracking
        // steps inside 200ms.
        Assert.IsGreaterThanOrEqualTo(200, elapsed);
    }

    [TestMethod]
    public void AllMatch_NonBacktrackingPattern_StaysLinearOnAdversarialInput()
    {
        // "(a+)+$" IS expressible under NonBacktracking, so it must never reach the timed fallback: the
        // adversarial tail then costs nothing, where backtracking would need longer than the age of the
        // universe at this input length. This is the guarantee the fallback must not erode.
        var start = Environment.TickCount64;

        Assert.IsFalse(RegexClauses.AllMatch([@"(a+)+$"], new string('a', 5_000) + "!"));

        Assert.IsLessThan(5_000, Environment.TickCount64 - start);
    }

    [TestMethod]
    public void AllMatch_OrdinaryPattern_IsUnaffected()
    {
        Assert.IsTrue(RegexClauses.AllMatch([@"^report\.md$"], "report.md"));
        Assert.IsFalse(RegexClauses.AllMatch([@"^report\.md$"], "notes.txt"));
        Assert.IsTrue(RegexClauses.AllMatch([@"\.exe$", "lertaro"], @"T:\lertaro\app.exe"));
    }
}
