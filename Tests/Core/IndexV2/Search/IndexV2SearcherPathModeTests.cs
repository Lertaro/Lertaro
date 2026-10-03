using static Lertaro.Core.Tests.IndexV2.Search.IndexV2SearcherFixture;

namespace Lertaro.Core.Tests.IndexV2.Search;

// Path-mode queries ("c:\Projects\", "projects\ readme") against the same sample drive, including how a
// drive token in the file part and the directory filter interact with the path reading.
[TestClass]
public sealed class IndexV2SearcherPathModeTests
{
    [TestMethod]
    public void SearchStreaming_PathModeQuery_ListsDirectoryItselfPlusChildren()
    {
        // A trailing-separator path lists the resolved directory itself alongside its children (see
        // PathSearch.TryDirectoryChildren) -- not just the children.
        using var fixture = BuildSampleDrive();

        var results = RunSearch(fixture, @"c:\Projects\");

        CollectionAssert.AreEquivalent(new[] { "Projects", "notes.md", "readme.txt" }, results.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public void SearchStreaming_PathModeQuery_ListsChildrenOfAnAddedDirectory()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            delta.Upsert(100, 2, "newdir", FileRecordFlags.Directory, 0, 0, 0, 0);
            delta.Upsert(101, 100, "inner.txt", FileRecordFlags.None, 0, 0, 0, 0);
        });

        var results = RunSearch(fixture, @"c:\projects\newdir\");

        CollectionAssert.AreEquivalent(new[] { "newdir", "inner.txt" }, results.Select(r => r.Name).ToArray());
    }

    [TestMethod]
    public void SearchStreaming_PathModeWithADriveInTheFilePart_StillMatches()
    {
        // Reported case: "projects\ readme c:" came back empty. The file part was parsed by a routine
        // with no notion of a drive, so "c:" stayed an ordinary term -- and a term containing a colon can
        // never match a file name, so one anywhere in the query took the whole thing to no results.
        using var fixture = BuildSampleDrive();

        var results = RunSearch(fixture, @"projects\ readme c:");

        Assert.HasCount(1, results);
        Assert.AreEqual("readme.txt", results[0].Name);
    }

    [TestMethod]
    public void SearchStreaming_PathModeWithAForeignDriveInTheFilePart_MatchesNothing()
    {
        // And it is a filter, not merely something to drop: naming a drive the results are not on has to
        // exclude them, the same as it does in a name-mode query.
        using var fixture = BuildSampleDrive();

        Assert.IsEmpty(RunSearch(fixture, @"projects\ readme z:"));
    }

    [TestMethod]
    public void SearchStreaming_PathModeWithADriveAndNoSpace_TreatsTheColonAsLiteralText()
    {
        // "c:readme" is no longer read as a drive spec -- the colon needs a space after it to be one.
        // Here the literal "c:readme" appears in no file name, so nothing matches.
        using var fixture = BuildSampleDrive();

        Assert.IsEmpty(RunSearch(fixture, @"projects\ c:readme"));
    }

    [TestMethod]
    public void SearchStreaming_PathModeDirectoryFilter_ExcludesAnotherDirectory()
    {
        using var fixture = BuildSampleDrive();

        Assert.IsEmpty(RunSearch(fixture, @"projects\ readme", directoryFilter: @"C:\Downloads"));
    }
}
