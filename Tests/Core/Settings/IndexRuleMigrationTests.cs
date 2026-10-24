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
        Assert.IsTrue(rules.Any(r => r.Pattern == IndexRuleDefaults.SystemSettingsLowPattern));
        // Retired: it demoted everything under an extensionless directory, e.g. D:\tools\**.
        Assert.IsFalse(rules.Any(r => r.Pattern == IndexRuleDefaults.RetiredExtensionlessRegex));
    }

    [TestMethod]
    public void Ensure_PreviousStockList_DropsTheRetiredRule()
    {
        // Write the list the previous build shipped, as a settings file would hold it.
        var settings = new UserSettings
        {
            IndexRules = IndexRuleDefaults.Create()
                .Append(new IndexRuleSetting
                {
                    Pattern = IndexRuleDefaults.RetiredExtensionlessRegex,
                    Effect = IndexRuleEffect.Low,
                })
                .ToList(),
        };

        var rules = IndexRuleMigration.Ensure(settings);

        Assert.HasCount(16, rules);
        Assert.IsFalse(rules.Any(r => r.Pattern == IndexRuleDefaults.RetiredExtensionlessRegex));
    }

    [TestMethod]
    public void Ensure_CustomisedList_StillDropsTheRetiredRule()
    {
        // Retirement is not a stock-list migration: the regex demoted whole trees, so a re-added copy is
        // removed from any list, whatever bucket the user put it in.
        var settings = new UserSettings
        {
            IndexRules = new List<IndexRuleSetting>
            {
                new() { Pattern = "node_modules", Effect = IndexRuleEffect.Exclude },
                new() { Pattern = IndexRuleDefaults.RetiredExtensionlessRegex, Effect = IndexRuleEffect.Low },
                new() { Pattern = "*.bak", Effect = IndexRuleEffect.Exclude },
            },
        };

        var rules = IndexRuleMigration.Ensure(settings);

        Assert.HasCount(3, rules);
        Assert.IsFalse(rules.Any(r => r.Pattern == IndexRuleDefaults.RetiredExtensionlessRegex));
        Assert.IsTrue(rules.Any(r => r.Pattern == IndexRuleDefaults.SystemSettingsLowPattern));
    }

    [TestMethod]
    [DataRow(IndexRuleEffect.Low)]
    [DataRow(IndexRuleEffect.Exclude)]
    [DataRow(IndexRuleEffect.High)]
    public void Ensure_RetiredRegex_IsRemovedRegardlessOfEffect(IndexRuleEffect effect)
    {
        var settings = new UserSettings
        {
            IndexRules = new List<IndexRuleSetting>
            {
                new() { Pattern = "node_modules", Effect = IndexRuleEffect.Exclude },
                new() { Pattern = "  " + IndexRuleDefaults.RetiredExtensionlessRegex + "  ", Effect = effect },
            },
        };

        var rules = IndexRuleMigration.Ensure(settings);

        Assert.IsFalse(rules.Any(r => IndexRuleSyntax.Clean(r.Pattern) == IndexRuleDefaults.RetiredExtensionlessRegex));
    }

    [TestMethod]
    public void Ensure_ExistingSystemSettingsDemotion_IsNotDuplicated()
    {
        var settings = new UserSettings
        {
            IndexRules = new List<IndexRuleSetting>
            {
                new() { Pattern = IndexRuleDefaults.SystemSettingsLowPattern, Effect = IndexRuleEffect.Low },
                new() { Pattern = "node_modules", Effect = IndexRuleEffect.Exclude },
            },
        };

        var rules = IndexRuleMigration.Ensure(settings);

        Assert.HasCount(2, rules);
        Assert.AreEqual(1, rules.Count(r => r.Pattern == IndexRuleDefaults.SystemSettingsLowPattern));
    }

    [TestMethod]
    public void Ensure_StockRules_DemoteSystemSettingsRowsInAnyLanguage()
    {
        var rules = ExclusionRuleSet.From(new UserSettings());

        Assert.AreEqual(IndexRuleEffect.Low,
            rules.GetPriority("__SEARCHABLE_ITEM__:SystemSettingsItemProvider:查看网络连接", isDirectory: false));
        Assert.AreEqual(IndexRuleEffect.Low,
            rules.GetPriority("__SEARCHABLE_ITEM__:SystemSettingsItemProvider:View network connections", isDirectory: false));
        Assert.AreEqual(IndexRuleEffect.Normal,
            rules.GetPriority("__SEARCHABLE_ITEM__:StartMenuAppItemProvider:Notepad", isDirectory: false));
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
