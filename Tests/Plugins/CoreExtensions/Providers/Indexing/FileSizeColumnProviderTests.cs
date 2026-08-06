using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.CoreExtensions.Providers.Indexing;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.Indexing;

[TestClass]
public sealed class FileSizeColumnProviderTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
        public FileMetadata Metadata { get; init; }
    }

    private static readonly FileSizeColumnProvider Provider = new();

    [TestMethod]
    public void GetColumns_ReturnsFileSizeAndExtensionColumns()
    {
        var ids = Provider.GetColumns().Select(c => c.ColumnId).ToList();

        CollectionAssert.AreEquivalent(new[] { "FileSize", "Extension" }, ids);
    }

    [TestMethod]
    public void GetCellValue_Directory_FileSizeColumnIsEmpty() =>
        Assert.AreEqual("", Provider.GetCellValue(new FakeResult { IsDir = true }, "FileSize"));

    [TestMethod]
    public void GetCellValue_UnknownMetadata_FileSizeColumnIsEmpty()
    {
        var result = new FakeResult { Metadata = new FileMetadata(100, default, DateTime.MinValue, default) };

        Assert.AreEqual("", Provider.GetCellValue(result, "FileSize"));
    }

    [TestMethod]
    [DataRow(500L, "500 B")]
    [DataRow(1536L, "1.5 KB")]
    [DataRow(3145728L, "3 MB")]
    public void GetCellValue_FileSize_FormatsHumanReadable(long bytes, string expected)
    {
        var result = new FakeResult { Metadata = new FileMetadata(bytes, default, DateTime.Now, default) };

        Assert.AreEqual(expected, Provider.GetCellValue(result, "FileSize"));
    }

    [TestMethod]
    public void GetCellValue_Directory_ExtensionColumnUsesFolderLabel() =>
        Assert.AreEqual("[Column_TypeFolder]", Provider.GetCellValue(new FakeResult { IsDir = true }, "Extension"));

    [TestMethod]
    public void GetCellValue_UnknownColumnId_ReturnsEmpty()
    {
        var result = new FakeResult { Metadata = new FileMetadata(100, default, DateTime.Now, default) };

        Assert.AreEqual("", Provider.GetCellValue(result, "Bogus"));
    }

    [TestMethod]
    public void SortComparer_ComparesByRawByteCountNotFormattedText()
    {
        var column = Provider.GetColumns().Single(c => c.ColumnId == "FileSize");
        var small = new FakeResult { Metadata = new FileMetadata(100, default, default, default) };
        var big = new FakeResult { Metadata = new FileMetadata(99_999, default, default, default) };

        Assert.IsLessThan(0, column.SortComparer!(small, big));
        Assert.IsGreaterThan(0, column.SortComparer!(big, small));
    }
}
