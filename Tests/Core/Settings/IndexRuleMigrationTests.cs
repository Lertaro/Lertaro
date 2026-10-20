namespace Lertaro.Core.Tests;

[TestClass]
public sealed class IndexRuleMigrationTests
{
    [TestMethod]
    public void Ensure_UntouchedDefaults_AdoptsTheNewDefaults()
    {
        var settings = new UserSettings();

        var rules = IndexRuleMigration.Ensure(settings);

        Assert.HasCount(16, rules);
        Assert.AreEqual(1, rules.Count(r => r.Effect == IndexRuleEffect.Exclude));
        Assert.AreEqual("node_modules", rules.Single(r => r.Effect == IndexRuleEffect.Exclude).Pattern);
        Assert.AreEqual(15, rules.Count(r => r.Effect == IndexRuleEffect.Low));
    }

    [TestMethod]
    public void Ensure_CustomLegacyLists_KeepEveryEntryAsExclusionAndAppendDefaults()
    {
        var settings = new UserSettings
        {
            ExcludedPaths = new List<string> { @"D:\cache" },
            IgnoredPathGlobs = new List<string> { "*.bak" },
            IgnoredPathRegexes = new List<string> { "^~" },
        };

        var rules = IndexRuleMigration.Ensure(settings);

        Assert.HasCount(3 + 15, rules);
        Assert.AreEqual(3, rules.Count(r => r.Effect == IndexRuleEffect.Exclude));
        Assert.IsTrue(rules.Any(r => r.Pattern == @"D:\cache" && r.Effect == IndexRuleEffect.Exclude));
        Assert.IsTrue(rules.Any(r => r.Pattern == "*.bak" && r.Effect == IndexRuleEffect.Exclude));
        Assert.IsTrue(rules.Any(r => r.Pattern == "/^~/" && r.Effect == IndexRuleEffect.Exclude));
    }

    [TestMethod]
    public void Ensure_LegacyRegexes_AreWrappedInDelimiters()
    {
        var settings = new UserSettings { IgnoredPathRegexes = new List<string> { "^\\.", "~$" } };

        var rules = IndexRuleMigration.Ensure(settings);

        var regexRules = rules.Where(r => r.Effect == IndexRuleEffect.Exclude).ToList();
        CollectionAssert.Contains(regexRules.Select(r => r.Pattern).ToList(), "/^\\./");
        CollectionAssert.Contains(regexRules.Select(r => r.Pattern).ToList(), "/~$/");
    }

    [TestMethod]
    public void Ensure_IsIdempotentAndKeepsTheSameList()
    {
        var settings = new UserSettings();

        var first = IndexRuleMigration.Ensure(settings);
        var second = IndexRuleMigration.Ensure(settings);

        Assert.AreSame(first, second);
        Assert.AreSame(first, settings.IndexRules);
    }

    [TestMethod]
    public void Ensure_ExplicitEmptyList_IsNotMigrated()
    {
        var settings = new UserSettings { IndexRules = new List<IndexRuleSetting>() };

        Assert.HasCount(0, IndexRuleMigration.Ensure(settings));
    }

    [TestMethod]
    public void MirrorLegacy_WritesOnlyTheExcludingSubset()
    {
        var settings = new UserSettings
        {
            IndexRules = new List<IndexRuleSetting>
            {
                new() { Pattern = @"C:\a", Effect = IndexRuleEffect.Exclude },
                new() { Pattern = "*.tmp", Effect = IndexRuleEffect.Exclude },
                new() { Pattern = @"/^\./", Effect = IndexRuleEffect.Exclude },
                new() { Pattern = @"build\", Effect = IndexRuleEffect.Low },
            },
        };

        IndexRuleMigration.MirrorLegacy(settings);

        CollectionAssert.AreEqual(new[] { @"C:\a" }, settings.ExcludedPaths);
        CollectionAssert.AreEqual(new[] { "*.tmp" }, settings.IgnoredPathGlobs);
        CollectionAssert.AreEqual(new[] { @"^\." }, settings.IgnoredPathRegexes);
    }

    [TestMethod]
    public void MirrorLegacy_NoRules_LeavesSettingsUntouched()
    {
        var settings = new UserSettings { ExcludedPaths = new List<string> { "keep" } };

        IndexRuleMigration.MirrorLegacy(settings);

        CollectionAssert.AreEqual(new[] { "keep" }, settings.ExcludedPaths);
    }
}
