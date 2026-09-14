using System.Text.Json;
using Lertaro.Cli.Search;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Cli.Tests.Search;

[TestClass]
public sealed class NonInteractiveSearchTests
{
    [TestMethod]
    public void ParserReadsSearchOptions()
    {
        var parsed = NonInteractiveSearchParser.TryParse(
            ["--search", "report", "--limit", "3", "--json", "--folders", "--path", @"C:\Data"],
            out var options,
            out var error);

        Assert.IsTrue(parsed);
        Assert.IsEmpty(error);
        Assert.IsNotNull(options);
        Assert.AreEqual("report", options.Query);
        Assert.AreEqual(3, options.Limit);
        Assert.IsTrue(options.Json);
        Assert.IsTrue(options.FoldersOnly);
        Assert.AreEqual(@"C:\Data", options.DirectoryFilter);
    }

    [TestMethod]
    public void ParserRejectsConflictingKindFilters()
    {
        var parsed = NonInteractiveSearchParser.TryParse(
            ["--search", "report", "--files", "--folders"],
            out var options,
            out var error);

        Assert.IsTrue(parsed);
        Assert.IsNull(options);
        Assert.AreEqual("--files and --folders cannot be used together.", error);
    }

    [TestMethod]
    public void PrepareFiltersBeforeApplyingLimit()
    {
        var options = new NonInteractiveSearchOptions("::expr", 2, false, true, false, null);
        var source = new[]
        {
            new SearchResult { Path = @"C:\one", IsDir = true },
            new SearchResult { Path = @"C:\two.txt", IsDir = false },
            new SearchResult { Path = @"C:\three.txt", IsDir = false }
        };

        var prepared = NonInteractiveSearchResults.Prepare(source, options.Query, options);

        Assert.HasCount(2, prepared);
        Assert.IsFalse(prepared[0].IsDir);
        Assert.AreEqual(@"C:\three.txt", prepared[1].Path);
    }

    [TestMethod]
    public void JsonIncludesRawMetadataFields()
    {
        var result = new SearchResult
        {
            Name = "report.txt",
            Path = @"C:\Data\report.txt",
            Drive = "C",
            Metadata = new FileMetadata(
                42,
                new DateTime(2026, 9, 14, 1, 2, 3),
                new DateTime(2026, 9, 14, 2, 3, 4),
                new DateTime(2026, 9, 14, 3, 4, 5))
        };

        using var document = JsonDocument.Parse(NonInteractiveSearchJson.Serialize([result]));
        var item = document.RootElement[0];

        Assert.AreEqual(42, item.GetProperty("size").GetInt64());
        Assert.AreEqual("C:\\Data\\report.txt", item.GetProperty("path").GetString());
        Assert.IsTrue(item.GetProperty("created").GetString()!.Contains("2026-09-14T01:02:03", StringComparison.Ordinal));
        Assert.IsTrue(item.GetProperty("modified").GetString()!.Contains("2026-09-14T02:03:04", StringComparison.Ordinal));
        Assert.IsTrue(item.GetProperty("accessed").GetString()!.Contains("2026-09-14T03:04:05", StringComparison.Ordinal));
    }
}
