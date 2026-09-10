using System.IO;
using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

// The inline window's fast "files right here" locator: one level, name-matched, hidden/system skipped.
[TestClass]
public sealed class DirectChildrenLocatorTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-direct-").FullName;

        public TempDirectory(params string[] names)
        {
            foreach (var name in names)
                File.WriteAllText(System.IO.Path.Combine(Path, name), string.Empty);
        }

        public void AddHidden(string name)
        {
            var full = System.IO.Path.Combine(Path, name);
            File.WriteAllText(full, string.Empty);
            File.SetAttributes(full, File.GetAttributes(full) | FileAttributes.Hidden);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    private static List<string> Locate(string directory, string query, int max = 50)
    {
        var matched = new List<string>();
        DirectChildrenLocator.MatchInto(directory, query, max, r => matched.Add(r.Name), CancellationToken.None);
        return matched;
    }

    [TestMethod]
    public void MatchInto_FindsDirectChildrenMatchingTheQuery()
    {
        using var dir = new TempDirectory("report-final.docx", "notes.txt", "report-draft.docx");

        var matched = Locate(dir.Path, "report");

        CollectionAssert.AreEquivalent(new[] { "report-final.docx", "report-draft.docx" }, matched);
    }

    [TestMethod]
    public void MatchInto_DoesNotDescendIntoSubdirectories()
    {
        using var dir = new TempDirectory("report.txt");
        Directory.CreateDirectory(System.IO.Path.Combine(dir.Path, "nested"));
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "nested", "report-deep.txt"), string.Empty);

        var matched = Locate(dir.Path, "report");

        CollectionAssert.AreEquivalent(new[] { "report.txt" }, matched);
    }

    [TestMethod]
    public void MatchInto_CaseIsIgnored()
    {
        using var dir = new TempDirectory("Report.TXT");

        CollectionAssert.AreEquivalent(new[] { "Report.TXT" }, Locate(dir.Path, "report"));
    }

    [TestMethod]
    public void MatchInto_SkipsHiddenEntries()
    {
        using var dir = new TempDirectory("report-visible.txt");
        dir.AddHidden("report-hidden.txt");

        var matched = Locate(dir.Path, "report");

        CollectionAssert.AreEquivalent(new[] { "report-visible.txt" }, matched);
    }

    [TestMethod]
    public void MatchInto_RespectsTheMatchCap()
    {
        using var dir = new TempDirectory("r1.txt", "r2.txt", "r3.txt");

        var matched = Locate(dir.Path, "r", max: 2);

        Assert.HasCount(2, matched);
    }

    [TestMethod]
    public void MatchInto_EmptyQuery_ReturnsNothing()
    {
        using var dir = new TempDirectory("report.txt");

        Assert.IsEmpty(Locate(dir.Path, ""));
    }

    [TestMethod]
    public void MatchInto_NonExistentDirectory_ReturnsNothing() =>
        Assert.IsEmpty(Locate(@"Z:\definitely-not-a-real-lertaro-dir", "report"));

    [TestMethod]
    public void MatchInto_ReportsDirectoriesAsWellAsFiles()
    {
        using var dir = new TempDirectory("report.txt");
        Directory.CreateDirectory(System.IO.Path.Combine(dir.Path, "report-folder"));

        var results = new List<Lertaro.Core.SearchResult>();
        DirectChildrenLocator.MatchInto(dir.Path, "report", 50, results.Add, CancellationToken.None);

        Assert.IsTrue(results.Any(r => r.Name == "report-folder" && r.IsDir));
        Assert.IsFalse(results.Single(r => r.Name == "report.txt").IsDir);
    }
}
