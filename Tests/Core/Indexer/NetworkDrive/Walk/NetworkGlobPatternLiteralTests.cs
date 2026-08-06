using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

// NetworkGlobPattern rejects a text that lacks the longest literal run its pattern requires, without
// running the compiled regex -- see its own comment for why (the exclusion rules are asked about every
// result on a drive and reject almost none of them). Getting the "required" literal wrong would not
// throw or look broken: the pattern would simply stop matching things it should, so a glob the user
// configured would quietly stop excluding them. Everything here is a pattern where a naive reading of
// the literal would be wrong.
[TestClass]
public sealed class NetworkGlobPatternLiteralTests
{
    private static void Matches(string glob, params string[] texts)
    {
        var pattern = new NetworkGlobPattern(glob);
        foreach (var text in texts)
            Assert.IsTrue(pattern.IsMatch(text), $"'{glob}' should match '{text}'");
    }

    private static void DoesNotMatch(string glob, params string[] texts)
    {
        var pattern = new NetworkGlobPattern(glob);
        foreach (var text in texts)
            Assert.IsFalse(pattern.IsMatch(text), $"'{glob}' should not match '{text}'");
    }

    [TestMethod]
    public void APlainLiteralGlob_StillMatches() => Matches("node_modules", @"c:\app\node_modules");

    [TestMethod]
    public void APlainLiteralGlob_StillRejects() => DoesNotMatch("node_modules", @"c:\app\src");

    [TestMethod]
    public void BraceAlternatives_AreNotTreatedAsRequired()
    {
        // "tmp" and "log" are alternatives, so neither is required and a literal taken from inside the
        // braces would reject the other one outright.
        Matches("*.{tmp,log}", "cache.tmp", "server.log");
        DoesNotMatch("*.{tmp,log}", "notes.txt");
    }

    [TestMethod]
    public void ACharacterClass_IsNotTreatedAsRequired()
    {
        Matches("[Tt]emp", @"c:\Temp", @"c:\temp");
        DoesNotMatch("[Tt]emp", @"c:\other");
    }

    [TestMethod]
    public void ALiteralSpanningAWildcard_IsNotJoinedAcrossIt() =>
        // "ab" and "cd" are separate runs; treating "abcd" as required would reject the very thing the
        // pattern exists to match.
        Matches("abc*cde", "abccde", "abcXXXcde");

    [TestMethod]
    public void ALiteralSpanningASeparator_IsNotJoinedAcrossIt() =>
        // A glob containing a separator is root-relative and therefore anchored (see GlobToRegex's
        // hasSlash branch), which is why ExclusionRuleSet tests one against the path relative to the
        // drive root as well as against the full path. Joining "build" and "output" into one required
        // literal would reject these before the regex ever got to decide.
        Matches("build/output", @"build\output", "build/output", @"c:\build\output");

    [TestMethod]
    public void TheLiteralIsMatchedCaseInsensitively() =>
        // The compiled regex ignores case, so the prefilter has to as well or it would reject matches
        // the pattern accepts.
        Matches("node_modules", @"c:\app\NODE_MODULES", @"c:\app\Node_Modules");

    [TestMethod]
    public void APatternWithNoUsefulLiteral_StillWorks()
    {
        // ".*" leaves only a one-character run, too short to be selective, so there is no prefilter and
        // the regex decides on its own.
        Matches(".*", @"c:\projects\.git");
        DoesNotMatch(".*", @"c:\projects\src");
    }

    [TestMethod]
    public void AllWildcards_StillWork()
    {
        Matches("*", "anything");
        Matches("**/*", @"c:\a\b");
    }

    [TestMethod]
    public void ContainingTheLiteralIsNotEnoughOnItsOwn() =>
        // The prefilter only ever rejects. A text that has the literal still has to satisfy the whole
        // pattern, or the prefilter would have turned into the answer.
        DoesNotMatch("build/output", @"c:\build\other", @"c:\output\build");

    [TestMethod]
    public void AnEmptyPattern_KeepsItsOldMeaning()
    {
        var pattern = new NetworkGlobPattern("");

        Assert.IsTrue(pattern.IsEmpty);
        Assert.IsTrue(pattern.IsMatch(""));
        Assert.IsFalse(pattern.IsMatch("anything"));
    }

    [TestMethod]
    public void QuestionMarkWildcards_DoNotExtendARun()
    {
        Matches("te?t.txt", "test.txt", "text.txt");
        DoesNotMatch("te?t.txt", "teeest.txt");
    }

    // A pattern shaped to backtrack: eight unbounded groups over the same character, anchored, against an
    // input that cannot satisfy the tail. Paired with a one-tick budget so the runaway path is reached
    // deterministically rather than by actually spending the real one.
    private const string RunawayGlob = "**a**a**a**a**a**a**a**a**z";
    private static readonly string RunawayInput = new string('a', 200) + "q";

    [TestMethod]
    public void APatternThatExceedsTheTimeout_IsAbandonedRatherThanRetriedPerEntry()
    {
        // The timeout is a per-match ceiling and this is asked about every entry on a drive, so without
        // retiring the pattern a runaway one costs the whole budget, and writes a log line, once per
        // entry -- millions of times.
        var pattern = new NetworkGlobPattern(RunawayGlob, TimeSpan.FromTicks(1));

        Assert.IsFalse(pattern.IsMatch(RunawayInput));
        Assert.IsTrue(pattern.IsAbandoned, "the pattern should have been retired on its first timeout");
    }

    [TestMethod]
    public void AnAbandonedPattern_StopsMatchingEntirely()
    {
        // Not merely "returns false for the input that timed out" -- it must stop running at all, which is
        // observable as it no longer matching something it otherwise would.
        var pattern = new NetworkGlobPattern(RunawayGlob, TimeSpan.FromTicks(1));
        pattern.IsMatch(RunawayInput);
        Assert.IsTrue(pattern.IsAbandoned);

        Assert.IsFalse(pattern.IsMatch("aaaaaaaaz"), "an abandoned pattern must not be run again");
    }

    [TestMethod]
    public void APatternWithinTheTimeout_IsNeverAbandoned()
    {
        var pattern = new NetworkGlobPattern("node_modules");

        Assert.IsTrue(pattern.IsMatch(@"c:\app\node_modules"));
        Assert.IsFalse(pattern.IsMatch(@"c:\app\src"));
        Assert.IsFalse(pattern.IsAbandoned);
    }
}
