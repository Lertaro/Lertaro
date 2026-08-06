using Lertaro.Core;
using Lertaro.App.ViewModels.Settings;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class SettingsChangeSnapshotTests
{
    [TestMethod]
    public void CaptureExclusions_CopiesCurrentListsFromSettings()
    {
        var settings = new UserSettings
        {
            ExcludedPaths = new List<string> { @"C:\a" },
            IgnoredPathGlobs = new List<string> { "*.tmp" },
            IgnoredPathRegexes = new List<string> { "^cache" },
        };

        var snapshot = SettingsChangeSnapshot.CaptureExclusions(settings);

        CollectionAssert.AreEqual(new[] { @"C:\a" }, snapshot.Paths.ToList());
        CollectionAssert.AreEqual(new[] { "*.tmp" }, snapshot.Globs.ToList());
        CollectionAssert.AreEqual(new[] { "^cache" }, snapshot.Regexes.ToList());
    }

    [TestMethod]
    public void ExclusionsChanged_IdenticalSnapshots_ReturnsFalse()
    {
        var settings = new UserSettings { ExcludedPaths = new List<string> { @"C:\a" } };
        var snapshot1 = SettingsChangeSnapshot.CaptureExclusions(settings);
        var snapshot2 = SettingsChangeSnapshot.CaptureExclusions(settings);

        Assert.IsFalse(SettingsChangeSnapshot.ExclusionsChanged(snapshot1, snapshot2));
    }

    [TestMethod]
    public void ExclusionsChanged_DifferentPaths_ReturnsTrue()
    {
        var oldSnapshot = SettingsChangeSnapshot.CaptureExclusions(new UserSettings { ExcludedPaths = new List<string> { @"C:\a" } });
        var newSnapshot = SettingsChangeSnapshot.CaptureExclusions(new UserSettings { ExcludedPaths = new List<string> { @"C:\b" } });

        Assert.IsTrue(SettingsChangeSnapshot.ExclusionsChanged(oldSnapshot, newSnapshot));
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
