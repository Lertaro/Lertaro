using Lertaro.Plugins.CoreExtensions.Providers.QuickPanel;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.QuickPanel;

// The history itself and the disk are both handed to Build, so what is tested here is the part that
// decides which entries survive and what the panel is told about them.
[TestClass]
public sealed class HistoryTabProviderTests
{
    [TestMethod]
    public void Build_KeepsTheOrderTheHistoryCameIn_AndAppliesTheCap()
    {
        var entries = HistoryTabProvider.Build(
            new[] { Entry(@"C:\newest"), Entry(@"C:\middle"), Entry(@"C:\oldest") },
            maxItems: 2,
            exists: _ => true);

        CollectionAssert.AreEqual(
            new[] { @"C:\newest", @"C:\middle" },
            entries.Select(entry => entry.FullPath).ToList());
    }

    [TestMethod]
    public void Build_SkipsWhatIsNoLongerOnDisk()
    {
        var entries = HistoryTabProvider.Build(
            new[] { Entry(@"C:\gone"), Entry(@"C:\here") },
            maxItems: 10,
            exists: path => path == @"C:\here");

        Assert.AreEqual(@"C:\here", entries.Single().FullPath);
    }

    // An application's path can be a virtual shell id, which is not a file and never will be.
    [TestMethod]
    public void Build_KeepsApplicationsWithoutCheckingTheDisk()
    {
        var entries = HistoryTabProvider.Build(
            new[] { Entry(@"shell:AppsFolder\Something!App", HistoryEntryKind.Application) },
            maxItems: 10,
            exists: _ => false);

        Assert.HasCount(1, entries);
        Assert.IsTrue(entries.Single().IsApplication);
    }

    [TestMethod]
    public void Build_CarriesWhenItWasOpenedAsTheModifiedTime()
    {
        var openedAt = new DateTimeOffset(2026, 7, 20, 9, 30, 0, TimeSpan.Zero);

        var entries = HistoryTabProvider.Build(
            new[] { Entry(@"C:\report", time: openedAt.ToUnixTimeSeconds()) }, maxItems: 10, exists: _ => true);

        Assert.AreEqual(openedAt.LocalDateTime, entries.Single().Metadata.Modified);
    }

    // The startup panel's own history has entries recorded before that field existed.
    [TestMethod]
    public void Build_AnEntryWithNoRecordedTime_HasNoModifiedTimeEither()
    {
        var entries = HistoryTabProvider.Build(
            new[] { Entry(@"C:\report", time: 0) }, maxItems: 10, exists: _ => true);

        Assert.AreEqual(default, entries.Single().Metadata.Modified);
    }

    [TestMethod]
    public void Build_IgnoresBlankPaths()
        => Assert.IsEmpty(HistoryTabProvider.Build(
            new[] { Entry("  ") }, maxItems: 10, exists: _ => true));

    [TestMethod]
    public void Build_EmptyHistory_IsEmpty()
        => Assert.IsEmpty(HistoryTabProvider.Build(
            Array.Empty<HistoryEntry>(), maxItems: 10, exists: _ => true));

    private static HistoryEntry Entry(string path, HistoryEntryKind kind = HistoryEntryKind.File, long time = 1)
        => new(Keyword: string.Empty, Path: path, Kind: kind, Time: time);
}
