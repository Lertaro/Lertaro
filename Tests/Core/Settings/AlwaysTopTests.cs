namespace Lertaro.Core.Tests;

[TestClass]
public sealed class AlwaysTopTests
{
    private static ExclusionRuleSet Rules(string root, params IndexRuleSetting[] rules)
        => ExclusionRuleSet.From(new UserSettings { IndexRules = rules.ToList() }, root);

    private static IndexRuleSetting High(string pattern, bool alwaysTop = false)
        => new() { Pattern = pattern, Effect = IndexRuleEffect.High, AlwaysTop = alwaysTop };

    private static IndexRuleSetting Rule(string pattern, IndexRuleEffect effect) => new() { Pattern = pattern, Effect = effect };

    private static ExclusionRuleSet.PriorityVerdict V(IndexRuleEffect effect, bool alwaysTop = false)
        => new(effect, alwaysTop);

    [TestMethod]
    public void PathRule_AlwaysTop_AppliesToDescendants()
    {
        var rules = Rules(@"C:\", High(@"C:\work", alwaysTop: true));

        Assert.AreEqual(V(IndexRuleEffect.High, true), rules.GetPriorityVerdict(@"C:\work\a\b.txt", isDirectory: false));
        Assert.AreEqual(V(IndexRuleEffect.High, true), rules.GetPriorityVerdict(@"C:\work", isDirectory: true));
        Assert.AreEqual(V(IndexRuleEffect.Normal), rules.GetPriorityVerdict(@"C:\other\f.txt", isDirectory: false));
    }

    [TestMethod]
    public void ChildPathRuleWithoutPin_ClearsAncestorPin()
    {
        var rules = Rules(@"C:\", High(@"C:\work", alwaysTop: true), High(@"C:\work\raw"));

        // The nearest path rule replaces the ancestor wholesale -- that is how a folder switches a pin off.
        Assert.AreEqual(V(IndexRuleEffect.High), rules.GetPriorityVerdict(@"C:\work\raw\x.txt", isDirectory: false));
        Assert.AreEqual(V(IndexRuleEffect.High, true), rules.GetPriorityVerdict(@"C:\work\x.txt", isDirectory: false));
    }

    [TestMethod]
    public void ChildNormalPathRule_ClearsAncestorPin()
    {
        var rules = Rules(@"C:\", High(@"C:\work", alwaysTop: true), Rule(@"C:\work\vendored", IndexRuleEffect.Normal));

        Assert.AreEqual(
            V(IndexRuleEffect.Normal),
            rules.GetPriorityVerdict(@"C:\work\vendored\x.txt", isDirectory: false));
    }

    [TestMethod]
    public void SiblingSubtree_KeepsAncestorPin()
    {
        var rules = Rules(@"C:\", High(@"C:\work", alwaysTop: true), Rule(@"C:\work\vendored", IndexRuleEffect.Normal));

        Assert.AreEqual(V(IndexRuleEffect.High, true), rules.GetPriorityVerdict(@"C:\work\other\x.txt", isDirectory: false));
    }

    [TestMethod]
    public void GlobRule_AlwaysTop_MatchesByLeafName()
    {
        var rules = Rules(@"C:\", High("*.ps1", alwaysTop: true));

        Assert.AreEqual(V(IndexRuleEffect.High, true), rules.GetPriorityVerdict(@"C:\p\a.ps1", isDirectory: false));
        Assert.AreEqual(V(IndexRuleEffect.Normal), rules.GetPriorityVerdict(@"C:\p\a.cs", isDirectory: false));
    }

    [TestMethod]
    public void RegexRule_AlwaysTop_MatchesByLeafName()
    {
        var rules = Rules(@"C:\", High(@"/^\./", alwaysTop: true));

        Assert.AreEqual(V(IndexRuleEffect.High, true), rules.GetPriorityVerdict(@"C:\p\.cache", isDirectory: true));
        Assert.AreEqual(V(IndexRuleEffect.Normal), rules.GetPriorityVerdict(@"C:\p\notes", isDirectory: true));
    }

    [TestMethod]
    public void EquallyStrongPatterns_PinnedOneWins()
    {
        // Both High patterns hit the same leaf; the pin from one of them must survive the merge.
        var rules = Rules(@"C:\", High("*.ps1"), High(@"/\.ps1$/", alwaysTop: true));

        Assert.AreEqual(V(IndexRuleEffect.High, true), rules.GetPriorityVerdict(@"C:\p\a.ps1", isDirectory: false));
    }

    [TestMethod]
    public void WeakerPattern_DoesNotClearStrongerPin()
    {
        var rules = Rules(@"C:\", High("*.ps1", alwaysTop: true), Rule("build", IndexRuleEffect.Low));

        Assert.AreEqual(V(IndexRuleEffect.High, true), rules.GetPriorityVerdict(@"C:\build\a.ps1", isDirectory: false));
    }

    [TestMethod]
    public void PathRuleWinsPinTieAgainstPattern()
    {
        // C:\work is an explicit High without the pin; the global *.ps1 High has it. The folder decides.
        var rules = Rules(@"C:\", High(@"C:\work"), High("*.ps1", alwaysTop: true));

        Assert.AreEqual(V(IndexRuleEffect.High), rules.GetPriorityVerdict(@"C:\work\a.ps1", isDirectory: false));
        // Away from the pinned folder the pattern's pin still applies.
        Assert.AreEqual(V(IndexRuleEffect.High, true), rules.GetPriorityVerdict(@"D:\tools\a.ps1", isDirectory: false));
    }

    [TestMethod]
    public void NonHighRuleWithFlag_IsNeverPinned()
    {
        var rules = Rules(
            @"C:\",
            new IndexRuleSetting { Pattern = @"C:\src", Effect = IndexRuleEffect.Normal, AlwaysTop = true },
            new IndexRuleSetting { Pattern = @"C:\tmp", Effect = IndexRuleEffect.Low, AlwaysTop = true },
            new IndexRuleSetting { Pattern = @"C:\junk", Effect = IndexRuleEffect.Exclude, AlwaysTop = true },
            High(@"C:\plain"));

        Assert.AreEqual(V(IndexRuleEffect.Normal), rules.GetPriorityVerdict(@"C:\src\a.cs", isDirectory: false));
        Assert.AreEqual(V(IndexRuleEffect.Low), rules.GetPriorityVerdict(@"C:\tmp\a.cs", isDirectory: false));
        Assert.AreEqual(V(IndexRuleEffect.Normal), rules.GetPriorityVerdict(@"C:\junk\a.cs", isDirectory: false));
        Assert.AreEqual(V(IndexRuleEffect.High), rules.GetPriorityVerdict(@"C:\plain\a.cs", isDirectory: false));
    }

    [TestMethod]
    public void PinnedVerdict_SurvivesDirectoryCache()
    {
        var rules = Rules(@"C:\", High(@"C:\work", alwaysTop: true));

        // First query walks and fills the per-directory cache; the second hits it, so the packed verdict
        // travels through Encode/Decode.
        Assert.AreEqual(V(IndexRuleEffect.High, true), rules.GetPriorityVerdict(@"C:\work\a\b.txt", isDirectory: false));
        Assert.AreEqual(V(IndexRuleEffect.High, true), rules.GetPriorityVerdict(@"C:\work\a\c\d.txt", isDirectory: false));
    }

    [TestMethod]
    public void Parse_FlagOnlySurvivesForHigh()
    {
        Assert.IsTrue(ParseAlwaysTop(@"C:\work", IndexRuleEffect.High, alwaysTop: true));
        Assert.IsFalse(ParseAlwaysTop(@"C:\work", IndexRuleEffect.High, alwaysTop: false));
        Assert.IsFalse(ParseAlwaysTop(@"C:\work", IndexRuleEffect.Normal, alwaysTop: true));
        Assert.IsFalse(ParseAlwaysTop(@"C:\work", IndexRuleEffect.Low, alwaysTop: true));
        Assert.IsFalse(ParseAlwaysTop(@"C:\work", IndexRuleEffect.Exclude, alwaysTop: true));
        // The pin is not tied to a path rule: a pattern rule can carry it too.
        Assert.IsTrue(ParseAlwaysTop(@"*.ps1", IndexRuleEffect.High, alwaysTop: true));

        // The four-argument overload keeps its old meaning: no pin.
        Assert.IsTrue(IndexRuleSyntax.TryParse(@"C:\work", IndexRuleEffect.High, out var parsed, out _));
        Assert.IsFalse(parsed.AlwaysTop);
        Assert.AreEqual(IndexRuleEffect.High, parsed.Effect);
    }

    [TestMethod]
    public void GetPriority_StillReturnsEffect()
    {
        var rules = Rules(@"C:\", High(@"C:\work", alwaysTop: true), Rule(@"C:\tmp", IndexRuleEffect.Low));

        Assert.AreEqual(IndexRuleEffect.High, rules.GetPriority(@"C:\work\a.txt", isDirectory: false));
        Assert.AreEqual(IndexRuleEffect.Low, rules.GetPriority(@"C:\tmp\a.txt", isDirectory: false));
        Assert.AreEqual(IndexRuleEffect.Normal, rules.GetPriority(@"C:\other\a.txt", isDirectory: false));
    }

    private static bool ParseAlwaysTop(string input, IndexRuleEffect effect, bool alwaysTop)
    {
        Assert.IsTrue(
            IndexRuleSyntax.TryParse(input, effect, alwaysTop, out var parsed, out var error),
            $"{input} / {effect} / {alwaysTop} failed to parse: {error}");
        return parsed.AlwaysTop;
    }
}
