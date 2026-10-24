using Lertaro.App.ViewModels.Settings;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class ExclusionSettingsViewModelTests
{
    // An empty rule list opts out of IndexRuleMigration's "untouched defaults" branch, which would
    // otherwise fill the list with the shipped low-priority defaults.
    private static UserSettings EmptySettings() => new() { IndexRules = new List<IndexRuleSetting>() };

    private static UserSettings SettingsWith(params IndexRuleSetting[] rules) => new() { IndexRules = rules.ToList() };

    private static List<ExclusionRuleItem> Visible(ExclusionSettingsViewModel vm) =>
        vm.RulesView.Cast<ExclusionRuleItem>().ToList();

    [TestMethod]
    public void Constructor_LoadsRulesWithEffects()
    {
        var vm = new ExclusionSettingsViewModel(SettingsWith(
            new IndexRuleSetting { Pattern = @"C:\temp", Effect = IndexRuleEffect.Exclude },
            new IndexRuleSetting { Pattern = "*.tmp", Effect = IndexRuleEffect.Low }));

        Assert.HasCount(3, vm.Rules);
        Assert.AreEqual(IndexRuleEffect.Exclude, vm.Rules.Single(r => r.Value == @"C:\temp").Effect);
        Assert.AreEqual(IndexRuleEffect.Low, vm.Rules.Single(r => r.Value == "*.tmp").Effect);
        // IndexRuleMigration adds the stock System Settings demotion to a saved list that predates it.
        Assert.AreEqual(IndexRuleEffect.Low,
            vm.Rules.Single(r => r.Value == IndexRuleDefaults.SystemSettingsLowPattern).Effect);
    }

    [TestMethod]
    public void AddRuleCommand_CanExecute_FollowsInputText()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings());

        Assert.IsFalse(vm.AddRuleCommand.CanExecute(null));

        vm.NewRuleText = @"C:\temp";

        Assert.IsTrue(vm.AddRuleCommand.CanExecute(null));
    }

    [TestMethod]
    public void AddRuleCommand_Execute_TrimsQuotesAndClearsInput()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings()) { NewRuleText = "  \"C:\\temp\"  " };

        vm.AddRuleCommand.Execute(null);

        Assert.AreEqual(@"C:\temp", vm.Rules[0].Value);
        Assert.AreEqual("", vm.NewRuleText);
    }

    [TestMethod]
    public void AddRuleCommand_ClassifiesGlobAndRegex()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings());

        vm.NewRuleText = "**\\node_modules\\**";
        vm.AddRuleCommand.Execute(null);
        vm.NewRuleText = "/^\\./";
        vm.AddRuleCommand.Execute(null);

        Assert.AreEqual(IndexRuleKind.Glob, vm.Rules[0].Kind);
        Assert.AreEqual(IndexRuleKind.Regex, vm.Rules[1].Kind);
        // The delimiters only mark the syntax; the stored pattern is the regex body.
        Assert.AreEqual(@"/^\./", vm.Rules[1].Value);
    }

    [TestMethod]
    public void AddRuleCommand_RejectsDuplicate()
    {
        var vm = new ExclusionSettingsViewModel(SettingsWith(
            new IndexRuleSetting { Pattern = "*.tmp" }));

        vm.NewRuleText = "*.TMP";
        vm.AddRuleCommand.Execute(null);

        Assert.HasCount(2, vm.Rules);
        Assert.IsTrue(vm.HasStatusMessage);
    }

    [TestMethod]
    public void AddRuleCommand_UsesSelectedNewRuleEffect()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings()) { NewRuleEffect = nameof(IndexRuleEffect.High) };

        vm.NewRuleText = "target\\";
        vm.AddRuleCommand.Execute(null);

        Assert.AreEqual(IndexRuleEffect.High, vm.Rules[0].Effect);
    }

    [TestMethod]
    public void AddRuleCommand_EffectPrefixOverridesPicker()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings()) { NewRuleEffect = nameof(IndexRuleEffect.High) };

        vm.NewRuleText = "!cache";
        vm.AddRuleCommand.Execute(null);

        Assert.AreEqual(IndexRuleEffect.Exclude, vm.Rules[0].Effect);
        Assert.AreEqual("cache", vm.Rules[0].Value);
    }

    [TestMethod]
    public void AddRuleCommand_EscapedPrefixStaysLiteral()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings());

        vm.NewRuleText = "\\!literal";
        vm.AddRuleCommand.Execute(null);

        Assert.AreEqual("!literal", vm.Rules[0].Value);
        Assert.AreEqual(IndexRuleEffect.Normal, vm.Rules[0].Effect);
    }

    [TestMethod]
    public void RemoveRuleCommand_RemovesItem()
    {
        var vm = new ExclusionSettingsViewModel(SettingsWith(
            new IndexRuleSetting { Pattern = "*.tmp" }));

        vm.RemoveRuleCommand.Execute(vm.Rules[0]);

        Assert.HasCount(1, vm.Rules);
        Assert.AreEqual(IndexRuleDefaults.SystemSettingsLowPattern, vm.Rules[0].Value);
    }

    [TestMethod]
    public void EditRuleCommand_StartsEditingWithCurrentValues()
    {
        var item = new ExclusionRuleItem("build\\", IndexRuleEffect.Low);
        var vm = new ExclusionSettingsViewModel(EmptySettings());
        vm.Rules.Add(item);

        vm.EditRuleCommand.Execute(item);

        Assert.IsTrue(item.IsEditing);
        Assert.AreEqual("build\\", item.EditValue);
        Assert.AreEqual(nameof(IndexRuleEffect.Low), item.EditEffectValue);
    }

    [TestMethod]
    public void SaveRuleCommand_CommitsBothTextAndEffect()
    {
        var item = new ExclusionRuleItem("*.tmp", IndexRuleEffect.Low) { IsEditing = true };
        var vm = new ExclusionSettingsViewModel(EmptySettings());
        vm.Rules.Add(item);
        item.EditValue = "*.bak";
        item.EditEffectValue = nameof(IndexRuleEffect.High);

        vm.SaveRuleCommand.Execute(item);

        Assert.IsFalse(item.IsEditing);
        Assert.AreEqual("*.bak", item.Value);
        Assert.AreEqual(IndexRuleEffect.High, item.Effect);
        Assert.AreEqual(IndexRuleKind.Glob, item.Kind);
    }

    [TestMethod]
    public void CancelRuleCommand_DiscardsEdits()
    {
        var item = new ExclusionRuleItem("*.tmp", IndexRuleEffect.Low) { IsEditing = true };
        var vm = new ExclusionSettingsViewModel(EmptySettings());
        vm.Rules.Add(item);
        item.EditValue = "*.bak";
        item.EditEffectValue = nameof(IndexRuleEffect.Exclude);

        vm.CancelRuleCommand.Execute(item);

        Assert.IsFalse(item.IsEditing);
        Assert.AreEqual("*.tmp", item.Value);
        Assert.AreEqual(IndexRuleEffect.Low, item.Effect);
    }

    [TestMethod]
    public void SaveRuleCommand_RejectsInvalidRegex()
    {
        var item = new ExclusionRuleItem("*.tmp") { IsEditing = true };
        var vm = new ExclusionSettingsViewModel(EmptySettings());
        vm.Rules.Add(item);
        item.EditValue = "/[/";

        vm.SaveRuleCommand.Execute(item);

        Assert.IsTrue(item.IsEditing);
        Assert.IsTrue(vm.HasStatusMessage);
    }

    [TestMethod]
    public void Filter_ShowsOnlyMatchingEffect()
    {
        var vm = new ExclusionSettingsViewModel(SettingsWith(
            new IndexRuleSetting { Pattern = "!a", Effect = IndexRuleEffect.Exclude },
            new IndexRuleSetting { Pattern = "b", Effect = IndexRuleEffect.Low },
            new IndexRuleSetting { Pattern = "c", Effect = IndexRuleEffect.High }));

        vm.Filter = nameof(IndexRuleEffect.Low);

        var visible = Visible(vm);
        Assert.HasCount(2, visible);
        Assert.IsTrue(visible.Any(r => r.Value == "b"));
        Assert.IsTrue(visible.Any(r => r.Value == IndexRuleDefaults.SystemSettingsLowPattern));

        vm.Filter = "All";
        Assert.HasCount(4, Visible(vm));
    }

    [TestMethod]
    public void SelectFilterCommand_SetsFilter()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings());

        vm.SelectFilterCommand.Execute(nameof(IndexRuleEffect.High));

        Assert.AreEqual(nameof(IndexRuleEffect.High), vm.Filter);
    }

    [TestMethod]
    public void AddRule_WhenFilteredToAnotherEffect_ReportsMove()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings()) { Filter = nameof(IndexRuleEffect.High) };
        vm.NewRuleText = "*.tmp";

        vm.AddRuleCommand.Execute(null);

        Assert.IsTrue(vm.HasStatusMessage);
        Assert.HasCount(0, Visible(vm));
    }

    [TestMethod]
    public void BulkText_Apply_ReadsEffectPrefixes()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings())
        {
            BulkText = "!C:\\cache\nbuild\\\n+target\\\n/^\\./",
        };

        vm.ApplyBulkTextCommand.Execute(null);

        Assert.HasCount(4, vm.Rules);
        Assert.AreEqual(IndexRuleEffect.Exclude, vm.Rules.Single(r => r.Value == @"C:\cache").Effect);
        Assert.AreEqual(IndexRuleEffect.Normal, vm.Rules.Single(r => r.Value == "build").Effect);
        Assert.AreEqual(IndexRuleEffect.High, vm.Rules.Single(r => r.Value == "target").Effect);
        var regex = vm.Rules.Single(r => r.Kind == IndexRuleKind.Regex);
        Assert.AreEqual(IndexRuleEffect.Normal, regex.Effect);
        Assert.AreEqual(@"/^\./", regex.Value);
    }

    [TestMethod]
    public void BulkText_RoundTripsPrefixesAndEscapes()
    {
        var vm = new ExclusionSettingsViewModel(SettingsWith(
            new IndexRuleSetting { Pattern = "!keep", Effect = IndexRuleEffect.Normal },
            new IndexRuleSetting { Pattern = "-low", Effect = IndexRuleEffect.Low }));

        var text = vm.BulkText;

        StringAssert.Contains(text, "\\!keep");
        StringAssert.Contains(text, "-low");
    }

    [TestMethod]
    public void BulkText_InvalidLineRejectsWholeBatch()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings()) { BulkText = "ok\n/[/" };

        vm.ApplyBulkTextCommand.Execute(null);

        Assert.HasCount(0, vm.Rules);
        Assert.IsTrue(vm.HasStatusMessage);
        Assert.AreEqual("ok\n/[/", vm.BulkText);
    }

    [TestMethod]
    public void Save_WritesUnifiedRules()
    {
        var settings = EmptySettings();
        var vm = new ExclusionSettingsViewModel(settings) { NewRuleText = @"C:\temp", NewRuleEffect = nameof(IndexRuleEffect.Exclude) };
        vm.AddRuleCommand.Execute(null);

        vm.Save();

        Assert.IsNotNull(settings.IndexRules);
        Assert.AreEqual(@"C:\temp", settings.IndexRules[0].Pattern);
        Assert.AreEqual(IndexRuleEffect.Exclude, settings.IndexRules[0].Effect);
    }

    [TestMethod]
    public void Conflict_FlagsPriorityRuleCoveredByExclusion()
    {
        var vm = new ExclusionSettingsViewModel(SettingsWith(
            new IndexRuleSetting { Pattern = @"C:\temp", Effect = IndexRuleEffect.Exclude },
            new IndexRuleSetting { Pattern = @"C:\temp\build", Effect = IndexRuleEffect.Low }));

        Assert.IsTrue(vm.HasConflicts);
        var priority = vm.Rules.Single(r => r.Value == @"C:\temp\build");
        Assert.IsTrue(priority.IsConflict);
        Assert.IsFalse(string.IsNullOrEmpty(priority.ConflictTooltip));
        Assert.IsFalse(vm.Rules.Single(r => r.Value == @"C:\temp").IsConflict);
    }

    [TestMethod]
    public void Conflict_SamePatternDifferentEffects_IsReported()
    {
        var vm = new ExclusionSettingsViewModel(SettingsWith(
            new IndexRuleSetting { Pattern = "node_modules", Effect = IndexRuleEffect.Exclude },
            new IndexRuleSetting { Pattern = "node_modules", Effect = IndexRuleEffect.Low }));

        Assert.IsTrue(vm.HasConflicts);
    }

    [TestMethod]
    public void Conflict_BroadLowRegexWithPathExclusion_IsNotFlagged()
    {
        var vm = new ExclusionSettingsViewModel(SettingsWith(
            new IndexRuleSetting { Pattern = @"C:\temp", Effect = IndexRuleEffect.Exclude },
            new IndexRuleSetting { Pattern = @"/^\d+$/", Effect = IndexRuleEffect.Low }));

        Assert.IsFalse(vm.HasConflicts);
    }

    [TestMethod]
    public void EffectChange_RecomputesConflictsAndCounts()
    {
        var vm = new ExclusionSettingsViewModel(SettingsWith(
            new IndexRuleSetting { Pattern = "cache", Effect = IndexRuleEffect.Exclude },
            new IndexRuleSetting { Pattern = "cache", Effect = IndexRuleEffect.Low }));
        var low = vm.Rules.Single(r => r.Effect == IndexRuleEffect.Low && r.Value == "cache");
        low.IsEditing = true;
        low.EditValue = "cache2";
        low.EditEffectValue = nameof(IndexRuleEffect.Normal);

        vm.SaveRuleCommand.Execute(low);

        Assert.IsFalse(vm.HasConflicts);
        Assert.IsFalse(low.IsConflict);
    }

    [TestMethod]
    public void Rules_SortPathsThenGlobsThenRegexWithinEachGroup()
    {
        var vm = new ExclusionSettingsViewModel(SettingsWith(
            new IndexRuleSetting { Pattern = "/z/" },
            new IndexRuleSetting { Pattern = "b*" },
            new IndexRuleSetting { Pattern = @"D:\x" },
            new IndexRuleSetting { Pattern = "a*" },
            new IndexRuleSetting { Pattern = @"C:\x" }));

        var ordered = Visible(vm).Select(r => r.Value).ToList();

        CollectionAssert.AreEqual(
            new[] { @"C:\x", @"D:\x", "a*", "b*", IndexRuleDefaults.SystemSettingsLowPattern, "/z/" },
            ordered);
    }

    [TestMethod]
    public void CountSummary_ReflectsEffects()
    {
        var vm = new ExclusionSettingsViewModel(SettingsWith(
            new IndexRuleSetting { Pattern = "a", Effect = IndexRuleEffect.Exclude },
            new IndexRuleSetting { Pattern = "b", Effect = IndexRuleEffect.Low },
            new IndexRuleSetting { Pattern = "c", Effect = IndexRuleEffect.Normal }));

        Assert.AreEqual(1, vm.Rules.Count(r => r.Effect == IndexRuleEffect.Exclude));
        Assert.AreEqual(2, vm.Rules.Count(r => r.Effect == IndexRuleEffect.Low));
        Assert.AreEqual(1, vm.Rules.Count(r => r.Effect == IndexRuleEffect.Normal));
        Assert.IsFalse(string.IsNullOrEmpty(vm.CountSummary));
    }

    private static IndexRuleSetting Rule(string pattern, IndexRuleEffect effect, bool alwaysTop = false) =>
        new() { Pattern = pattern, Effect = effect, AlwaysTop = alwaysTop };

    [TestMethod]
    public void Save_WritesAlwaysTopForHighRules()
    {
        var settings = SettingsWith(Rule(@"C:\work", IndexRuleEffect.High, alwaysTop: true));
        var vm = new ExclusionSettingsViewModel(settings);

        Assert.IsTrue(vm.Rules.Single(r => r.Value == @"C:\work").AlwaysTop);

        vm.Save();

        Assert.IsTrue(settings.IndexRules.Single(r => r.Pattern == @"C:\work").AlwaysTop);
    }

    [TestMethod]
    public void Save_DropsPinStoredOnANonHighRule()
    {
        // A pin in the configuration on a rule that is not high never reaches the view or the saved list,
        // rather than being kept and ignored by Core.
        var settings = SettingsWith(Rule(@"C:\tmp", IndexRuleEffect.Low, alwaysTop: true));
        var vm = new ExclusionSettingsViewModel(settings);
        var item = vm.Rules.Single(r => r.Value == @"C:\tmp");

        Assert.IsFalse(item.AlwaysTop);
        Assert.IsFalse(item.CanPin);

        vm.Save();

        Assert.IsFalse(settings.IndexRules.Single(r => r.Pattern == @"C:\tmp").AlwaysTop);
    }

    [TestMethod]
    public void EffectChange_ClearsPinAndCanPin()
    {
        var vm = new ExclusionSettingsViewModel(SettingsWith(Rule(@"C:\work", IndexRuleEffect.High, alwaysTop: true)));
        var item = vm.Rules.Single(r => r.Value == @"C:\work");

        item.Effect = IndexRuleEffect.Low;
        Assert.IsFalse(item.AlwaysTop);
        Assert.IsFalse(item.CanPin);

        item.Effect = IndexRuleEffect.High;
        Assert.IsFalse(item.AlwaysTop, "Coming back to high does not restore the pin; the user re-checks it.");
        Assert.IsTrue(item.CanPin);
    }

    [TestMethod]
    public void BulkApply_KeepsThePinOnUnchangedHighRules()
    {
        var vm = new ExclusionSettingsViewModel(SettingsWith(
            Rule(@"C:\work", IndexRuleEffect.High, alwaysTop: true),
            Rule(@"C:\tools", IndexRuleEffect.High)));

        vm.BulkText = "+C:\\work\n+C:\\tools";
        vm.ApplyBulkTextCommand.Execute(null);

        Assert.IsTrue(vm.Rules.Single(r => r.Value == @"C:\work").AlwaysTop);
        Assert.IsFalse(vm.Rules.Single(r => r.Value == @"C:\tools").AlwaysTop);
    }

    [TestMethod]
    public void BulkApply_RewrittenRuleLosesItsPin()
    {
        var vm = new ExclusionSettingsViewModel(SettingsWith(Rule(@"C:\work", IndexRuleEffect.High, alwaysTop: true)));

        vm.BulkText = "+C:\\work\\sub";
        vm.ApplyBulkTextCommand.Execute(null);

        Assert.IsFalse(vm.Rules.Single(r => r.Value == @"C:\work\sub").AlwaysTop);
    }

    [TestMethod]
    public void SaveEdit_ChangingLevelAwayFromHigh_ClearsPin()
    {
        var settings = SettingsWith(Rule(@"C:\work", IndexRuleEffect.High, alwaysTop: true));
        var vm = new ExclusionSettingsViewModel(settings);
        var item = vm.Rules.Single(r => r.Value == @"C:\work");

        vm.EditRuleCommand.Execute(item);
        item.EditEffectValue = nameof(IndexRuleEffect.Normal);
        vm.SaveRuleCommand.Execute(item);

        Assert.IsFalse(item.AlwaysTop);

        vm.Save();

        Assert.IsFalse(settings.IndexRules.Single(r => r.Pattern == @"C:\work").AlwaysTop);
    }

    [TestMethod]
    public void Save_Reload_RoundTripsPin()
    {
        var settings = SettingsWith(Rule(@"C:\work", IndexRuleEffect.High, alwaysTop: true));

        new ExclusionSettingsViewModel(settings).Save();

        Assert.IsTrue(new ExclusionSettingsViewModel(settings).Rules.Single(r => r.Value == @"C:\work").AlwaysTop);
    }
}
