using System.Reflection;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;

namespace Lertaro.App.Tests.Models;

// A row built from a search keeps the index record and derives what it displays from that, instead of
// copying the values out at construction. Copying them cost 577 bytes a row, of which 353 were strings
// -- held for the life of the search across six hundred thousand rows the grid never realizes.
[TestClass]
public sealed class AppSearchResultTests
{
    private static SearchResult Record(string path, string name, bool isDir = false, string drive = "D") =>
        new() { Path = path, Name = name, IsDir = isDir, Drive = drive };

    private static AppSearchResult RowFor(SearchResult record, string query = "q", string? scope = null) =>
        SearchResultMapper.CreateUiResult(record, query, index: 0, isApplication: false, scope);

    // Whether the row has allocated its side object yet. The whole point of the change is that a row
    // nobody looks at never does, so it is asserted directly rather than inferred from a memory number.
    private static bool HasExtras(AppSearchResult row) =>
        typeof(AppSearchResult)
            .GetField("_extras", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(row) != null;

    [TestMethod]
    public void ARowNobodyLooksAt_AllocatesNothingBeyondItself()
    {
        var row = RowFor(Record(@"D:\Projects\app\readme.md", "readme.md"));

        Assert.IsFalse(HasExtras(row), "constructing a row must not allocate anything it may never be asked for");
    }

    [TestMethod]
    public void ARowDerivesItsDisplayedValuesFromTheRecord()
    {
        var row = RowFor(Record(@"D:\Projects\app\readme.md", "readme.md"));

        Assert.AreEqual("readme.md", row.Name);
        Assert.AreEqual(@"D:\Projects\app\readme.md", row.FullPath);
        Assert.AreEqual(@"D:\Projects\app", row.ParentDir);
        Assert.AreEqual(@"D:\Projects\app", row.ContextDirectory);
        Assert.AreEqual("D", row.Drive);
        Assert.IsFalse(row.IsDir);
        Assert.AreEqual("File", row.ResultKind);
        Assert.AreEqual("q", row.SearchQuery);
    }

    [TestMethod]
    public void FullPathAndName_AreTheRecordsOwnStrings_NotCopies()
    {
        // The saving is not merely "fewer bytes" -- these must be the SAME instances the record already
        // holds, or the row is still paying for a duplicate of every path on the drive.
        var record = Record(@"D:\Projects\app\readme.md", "readme.md");
        var row = RowFor(record);

        Assert.IsTrue(ReferenceEquals(record.Path, row.FullPath));
        Assert.IsTrue(ReferenceEquals(record.Name, row.Name));
    }

    [TestMethod]
    public void ARecordWithNoName_FallsBackToItsPath()
    {
        var row = RowFor(Record(@"D:\Projects\app", "   "));

        Assert.AreEqual(@"D:\Projects\app", row.Name);
    }

    [TestMethod]
    public void ADirectorysContextDirectory_IsItself()
    {
        var row = RowFor(Record(@"D:\Projects\app", "app", isDir: true));

        Assert.AreEqual(@"D:\Projects\app", row.ContextDirectory);
    }

    [TestMethod]
    public void ARootLevelFile_FallsBackToItsDriveForContext()
    {
        var row = RowFor(Record(@"D:\", "D:\\", drive: "D"));

        // GetDirectoryName of a root returns null; the drive is the answer rather than an empty string.
        Assert.AreEqual(@"D:\", row.ContextDirectory);
    }

    [TestMethod]
    public void ParentDir_IsDerivedOnceAndThenCached()
    {
        var row = RowFor(Record(@"D:\Projects\app\readme.md", "readme.md"));

        var first = row.ParentDir;
        var second = row.ParentDir;

        // Several bindings on a realized row read this; re-deriving per read would run GetDirectoryName
        // over and over and allocate a fresh string each time.
        Assert.IsTrue(ReferenceEquals(first, second));
        Assert.IsTrue(HasExtras(row), "the cache lives in the side object, so reading it allocates one");
    }

    [TestMethod]
    public void ASyntheticRowsOwnValues_WinOverTheRecord()
    {
        // Section headers, "no results", plugin actions and favorites are all built this way, with no
        // record behind them at all.
        var row = new AppSearchResult
        {
            Name = "Applications",
            FullPath = "__SECTION_HEADER__",
            ParentDir = string.Empty,
            IsDir = false,
            Drive = string.Empty,
            ResultKind = "SectionHeader",
            SearchQuery = "q"
        };

        Assert.AreEqual("Applications", row.Name);
        Assert.AreEqual("__SECTION_HEADER__", row.FullPath);
        Assert.AreEqual(string.Empty, row.ParentDir);
        Assert.AreEqual(string.Empty, row.Drive);
        Assert.IsTrue(row.IsSearchSectionHeader);
    }

    [TestMethod]
    public void ASetValue_OverridesWhatTheRecordSays()
    {
        var row = RowFor(Record(@"D:\Projects\app\readme.md", "readme.md"));

        row.Name = "renamed";
        row.ParentDir = "elsewhere";

        Assert.AreEqual("renamed", row.Name);
        Assert.AreEqual("elsewhere", row.ParentDir);
        Assert.AreEqual(@"D:\Projects\app\readme.md", row.FullPath, "an untouched property still comes from the record");
    }

    [TestMethod]
    public void DateModified_AnswersFromTheRecordsMetadata()
    {
        var when = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var record = Record(@"D:\Projects\app\readme.md", "readme.md");
        record.Metadata = new PluginSdk.Abstractions.FileMetadata { Modified = when };

        var row = RowFor(record);

        Assert.AreEqual(when, row.DateModified);
        Assert.AreEqual(when, row.Metadata.Modified);
    }

    [TestMethod]
    public void DateModified_WslWithoutIndexedValue_DoesNotStartFilesystemFallback()
    {
        var row = RowFor(Record(@"\\wsl$\Ubuntu\home\testuser\readme.md", "readme.md"));

        Assert.AreEqual(DateTime.MinValue, row.DateModified);
        Assert.IsFalse(HasExtras(row), "WSL display metadata must remain index-only");
    }

    [TestMethod]
    public void DefaultsAreReturnedWithoutAllocating()
    {
        var row = RowFor(Record(@"D:\Projects\app\readme.md", "readme.md"));

        Assert.AreEqual(string.Empty, row.ShortcutHint);
        Assert.AreEqual(System.Windows.Visibility.Collapsed, row.ShortcutVisibility);
        Assert.AreEqual("Copy", row.InstantResultActionType);
        Assert.AreEqual(string.Empty, row.InstantResultActionArgument);
        Assert.AreEqual(0u, row.PluginActionId);
        Assert.IsNull(row.IconOverride);
        Assert.IsNull(row.SourceProvider);
        Assert.IsNull(row.TabCompletion);

        Assert.IsFalse(HasExtras(row), "reading a default must not allocate the side object");
    }

    [TestMethod]
    public void SettingAShortcutHintToItsDefault_ChangesNothing()
    {
        var row = RowFor(Record(@"D:\Projects\app\readme.md", "readme.md"));

        row.ShortcutHint = string.Empty;
        row.ShortcutVisibility = System.Windows.Visibility.Collapsed;

        Assert.IsFalse(HasExtras(row), "writing the value it already reports is not a change");
    }

    [TestMethod]
    public void AScopedSearchsRow_ShowsItsParentRelativeToTheScope()
    {
        var row = RowFor(Record(@"D:\Projects\app\src\main.cs", "main.cs"), scope: @"D:\Projects\app");

        // The quick window's scoped search shows where a hit sits inside the scope, not its whole path.
        Assert.AreNotEqual(@"D:\Projects\app\src", row.ParentDir);
        Assert.Contains("src", row.ParentDir);
    }
}
