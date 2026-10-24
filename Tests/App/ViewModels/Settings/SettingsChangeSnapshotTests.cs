using Lertaro.Core;
using Lertaro.App.ViewModels.Settings;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class SettingsChangeSnapshotTests
{
    [TestMethod]
    public void CaptureExclusions_CopiesCurrentRulesFromSettings()
    {
        var settings = new UserSettings
        {
            IndexRules = new List<IndexRuleSetting>
            {
                new() { Pattern = @"C:\a", Effect = IndexRuleEffect.Exclude },
                new() { Pattern = "*.tmp", Effect = IndexRuleEffect.Low },
            },
        };

        var snapshot = SettingsChangeSnapshot.CaptureExclusions(settings);

        // The stock System Settings demotion is part of the snapshot: CaptureExclusions reads the same
        // normalized list the index would use, not the raw saved one.
        CollectionAssert.AreEqual(new[] { @"C:\a", "*.tmp", IndexRuleDefaults.SystemSettingsLowPattern },
            snapshot.Rules.Select(r => r.Pattern).ToList());
        CollectionAssert.AreEqual(
            new[] { IndexRuleEffect.Exclude, IndexRuleEffect.Low, IndexRuleEffect.Low },
            snapshot.Rules.Select(r => r.Effect).ToList());
    }

    [TestMethod]
    public void ExclusionsChanged_IdenticalSnapshots_ReturnsFalse()
    {
        var settings = new UserSettings { IndexRules = new List<IndexRuleSetting> { new() { Pattern = @"C:\a", Effect = IndexRuleEffect.Exclude } } };
        var snapshot1 = SettingsChangeSnapshot.CaptureExclusions(settings);
        var snapshot2 = SettingsChangeSnapshot.CaptureExclusions(settings);

        Assert.IsFalse(SettingsChangeSnapshot.ExclusionsChanged(snapshot1, snapshot2));
    }

    [TestMethod]
    public void ExclusionsChanged_DifferentPaths_ReturnsTrue()
    {
        var oldSnapshot = SettingsChangeSnapshot.CaptureExclusions(new UserSettings { IndexRules = new List<IndexRuleSetting> { new() { Pattern = @"C:\a", Effect = IndexRuleEffect.Exclude } } });
        var newSnapshot = SettingsChangeSnapshot.CaptureExclusions(new UserSettings { IndexRules = new List<IndexRuleSetting> { new() { Pattern = @"C:\b", Effect = IndexRuleEffect.Exclude } } });

        Assert.IsTrue(SettingsChangeSnapshot.ExclusionsChanged(oldSnapshot, newSnapshot));
    }

    [TestMethod]
    public void ExclusionsChanged_PriorityOnlyChange_ReturnsFalse()
    {
        var oldSnapshot = SettingsChangeSnapshot.CaptureExclusions(new UserSettings { IndexRules = new List<IndexRuleSetting> { new() { Pattern = "build\\", Effect = IndexRuleEffect.Low } } });
        var newSnapshot = SettingsChangeSnapshot.CaptureExclusions(new UserSettings { IndexRules = new List<IndexRuleSetting> { new() { Pattern = "build\\", Effect = IndexRuleEffect.High } } });

        Assert.IsFalse(SettingsChangeSnapshot.ExclusionsChanged(oldSnapshot, newSnapshot));
    }

    [TestMethod]
    public void StringListChanged_SameItemsDifferentOrder_ReturnsFalse() =>
        Assert.IsFalse(SettingsChangeSnapshot.StringListChanged(new[] { "a", "b" }, new[] { "b", "a" }));

    [TestMethod]
    public void StringListChanged_CaseDifferenceOnly_ReturnsFalse() =>
        Assert.IsFalse(SettingsChangeSnapshot.StringListChanged(new[] { "a" }, new[] { "A" }));

    [TestMethod]
    public void StringListChanged_WhitespaceOnlyEntriesIgnored_ReturnsFalse() =>
        Assert.IsFalse(SettingsChangeSnapshot.StringListChanged(new[] { "a", "  " }, new[] { "a" }));

    [TestMethod]
    public void StringListChanged_UntrimmedWhitespaceIgnored_ReturnsFalse() =>
        Assert.IsFalse(SettingsChangeSnapshot.StringListChanged(new[] { "  a  " }, new[] { "a" }));

    [TestMethod]
    public void StringListChanged_DifferentCounts_ReturnsTrue() =>
        Assert.IsTrue(SettingsChangeSnapshot.StringListChanged(new[] { "a" }, new[] { "a", "b" }));

    [TestMethod]
    public void StringListChanged_DifferentValues_ReturnsTrue() =>
        Assert.IsTrue(SettingsChangeSnapshot.StringListChanged(new[] { "a" }, new[] { "b" }));
}
