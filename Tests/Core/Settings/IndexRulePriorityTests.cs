namespace Lertaro.Core.Tests;

[TestClass]
public sealed class IndexRulePriorityTests
{
    private static ExclusionRuleSet Rules(string root, params IndexRuleSetting[] rules)
    {
        var settings = new UserSettings { IndexRules = rules.ToList() };
        return ExclusionRuleSet.From(settings, root);
    }

    private static IndexRuleSetting Rule(string pattern, IndexRuleEffect effect) => new() { Pattern = pattern, Effect = effect };

    [TestMethod]
    public void NoPriorityRules_ReturnsNormal()
    {
        var rules = Rules(@"C:\", Rule("node_modules", IndexRuleEffect.Exclude));

        Assert.AreEqual(IndexRuleEffect.Normal, rules.GetPriority(@"C:\proj\file.txt", isDirectory: false));
    }

    [TestMethod]
    public void PathRule_AppliesToItselfAndDescendants()
    {
        var rules = Rules(@"C:\", Rule(@"C:\build", IndexRuleEffect.Low));

        Assert.AreEqual(IndexRuleEffect.Low, rules.GetPriority(@"C:\build", isDirectory: true));
        Assert.AreEqual(IndexRuleEffect.Low, rules.GetPriority(@"C:\build\deep\file.obj", isDirectory: false));
        Assert.AreEqual(IndexRuleEffect.Normal, rules.GetPriority(@"C:\src\file.cs", isDirectory: false));
    }

    [TestMethod]
    public void HighPathRule_AppliesToFilesInsideIt()
    {
        var settings = new UserSettings
        {
            IndexRules = new List<IndexRuleSetting> { Rule(@"D:\RegCoolX64", IndexRuleEffect.High) },
        };
        var rules = ExclusionRuleSet.From(settings);

        Assert.AreEqual(IndexRuleEffect.High, rules.GetPriority(@"D:\RegCoolX64\Readme.txt", isDirectory: false));
        Assert.AreEqual(IndexRuleEffect.High, rules.GetPriority(@"D:\RegCoolX64\Settings\App.json", isDirectory: false));
        Assert.AreEqual(IndexRuleEffect.Normal, rules.GetPriority(@"D:\Other\Readme.txt", isDirectory: false));
    }

    [TestMethod]
    public void HasHighPriorityRules_OnlyWhenAHighRuleExists()
    {
        var normal = Rules(@"C:\", Rule("node_modules", IndexRuleEffect.Exclude), Rule(@"C:\build", IndexRuleEffect.Low));
        Assert.IsFalse(normal.HasHighPriorityRules);

        var high = Rules(@"C:\", Rule(@"C:\RegCoolX64", IndexRuleEffect.High));
        Assert.IsTrue(high.HasHighPriorityRules);
    }

    [TestMethod]
    public void NearestAncestorWins()
    {
        var rules = Rules(@"C:\",
            Rule(@"C:\p", IndexRuleEffect.Low),
            Rule(@"C:\p\q", IndexRuleEffect.High));

        Assert.AreEqual(IndexRuleEffect.High, rules.GetPriority(@"C:\p\q\r\file.txt", isDirectory: false));
        Assert.AreEqual(IndexRuleEffect.Low, rules.GetPriority(@"C:\p\other\file.txt", isDirectory: false));
    }

    [TestMethod]
    public void ExplicitNormalStopsInheritance()
    {
        var rules = Rules(@"C:\",
            Rule(@"C:\p", IndexRuleEffect.Low),
            Rule(@"C:\p\q", IndexRuleEffect.Normal));

        Assert.AreEqual(IndexRuleEffect.Normal, rules.GetPriority(@"C:\p\q\file.txt", isDirectory: false));
        Assert.AreEqual(IndexRuleEffect.Low, rules.GetPriority(@"C:\p\r\file.txt", isDirectory: false));
    }

    [TestMethod]
    public void HighBeatsLowAndNormalAtTheSameLevel()
    {
        var rules = Rules(@"C:\",
            Rule(@"/^[^.]+$/", IndexRuleEffect.Low),
            Rule(@"C:\proj", IndexRuleEffect.High));

        Assert.AreEqual(IndexRuleEffect.High, rules.GetPriority(@"C:\proj\src", isDirectory: true));
    }

    [TestMethod]
    public void RegexRule_MatchesLeafName()
    {
        var rules = Rules(@"C:\", Rule(@"/^\./", IndexRuleEffect.Low));

        Assert.AreEqual(IndexRuleEffect.Low, rules.GetPriority(@"C:\proj\.git", isDirectory: true));
        Assert.AreEqual(IndexRuleEffect.Low, rules.GetPriority(@"C:\proj\.gitignore", isDirectory: false));
        Assert.AreEqual(IndexRuleEffect.Normal, rules.GetPriority(@"C:\proj\git", isDirectory: true));
    }

    [TestMethod]
    public void RegexRule_DoesNotMatchTheAbsolutePathBody()
    {
        var rules = Rules(@"C:\proj", Rule(@"/^C:/", IndexRuleEffect.Low));

        // The drive prefix only exists in the absolute path; leaf and root-relative text must not see it.
        Assert.AreEqual(IndexRuleEffect.Normal, rules.GetPriority(@"C:\proj\file.txt", isDirectory: false));
    }

    [TestMethod]
    public void GlobRule_MatchesRelativePathUnderRoot()
    {
        var rules = Rules(@"C:\proj", Rule(@"build\**", IndexRuleEffect.Low));

        Assert.AreEqual(IndexRuleEffect.Low, rules.GetPriority(@"C:\proj\build\out\x.dll", isDirectory: false));
    }

    [TestMethod]
    public void ExcludedPaths_DoNotAffectPriority()
    {
        var rules = Rules(@"C:\",
            Rule("node_modules", IndexRuleEffect.Exclude),
            Rule(@"C:\proj", IndexRuleEffect.High));

        Assert.AreEqual(IndexRuleEffect.High, rules.GetPriority(@"C:\proj\a.txt", isDirectory: false));
    }

    [TestMethod]
    public void BlankPath_ReturnsNormal()
    {
        var rules = Rules(@"C:\", Rule(@"C:\build", IndexRuleEffect.Low));

        Assert.AreEqual(IndexRuleEffect.Normal, rules.GetPriority("", isDirectory: false));
    }

    [TestMethod]
    public void HighPriorityRoots_ListsResolvedHighPathRulesOnly()
    {
        var rules = Rules(@"C:\",
            Rule(@"C:\High\Deep", IndexRuleEffect.High),
            Rule(@"C:\High\Deep", IndexRuleEffect.High),
            Rule(@"C:\Low", IndexRuleEffect.Low),
            Rule(@"build\**", IndexRuleEffect.High),
            Rule("/^tmp/", IndexRuleEffect.High));

        CollectionAssert.AreEqual(new[] { @"C:\High\Deep" }, rules.HighPriorityRoots.ToArray());
    }

    [TestMethod]
    public void HighPriorityRoots_SkipsDriveRoots()
    {
        // Promoting a whole volume would only make every query scan it a second time.
        var rules = Rules(@"C:\", Rule(@"C:\", IndexRuleEffect.High));

        Assert.AreEqual(0, rules.HighPriorityRoots.Count);
    }

    [TestMethod]
    public void StartMenu_DoesNotInheritStockAncestorLow_UnlessExplicitlyDemoted()
    {
        var stockRules = Rules(
            @"C:\",
            Rule(@"C:\ProgramData", IndexRuleEffect.Low),
            Rule(@"C:\Users\Demo\AppData", IndexRuleEffect.Low),
            Rule(@"C:\Users\Demo\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Bloatware", IndexRuleEffect.Low));

        Assert.AreEqual(
            IndexRuleEffect.Low,
            stockRules.GetPriority(@"C:\Users\Demo\AppData\Local\Temp\cache.bin", isDirectory: false));
        Assert.AreEqual(
            IndexRuleEffect.Normal,
            stockRules.GetPriority(@"C:\Users\Demo\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\PixPin\PixPin.lnk", isDirectory: false));
        Assert.AreEqual(
            IndexRuleEffect.Normal,
            stockRules.GetPriority(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\7-Zip\7-Zip.lnk", isDirectory: false));
        Assert.AreEqual(
            IndexRuleEffect.Low,
            stockRules.GetPriority(@"C:\Users\Demo\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Bloatware\Updater.lnk", isDirectory: false));
    }
}
