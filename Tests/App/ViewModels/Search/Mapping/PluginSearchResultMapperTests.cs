using System.IO;
using Lertaro.App;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class PluginSearchResultMapperTests
{
    [TestMethod]
    public void AddInstantResultItems_RealFile_MapsAsFileResult()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"mapper_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "x");
        try
        {
            var item = new InstantResultItem
            {
                Title = "mapper-test",
                ActionType = "Execute",
                ActionArgument = tempFile,
                Description = Path.GetDirectoryName(tempFile) ?? string.Empty
            };

            var rows = new List<AppSearchResult>();
            PluginSearchResultMapper.AddInstantResultItems(rows, new[] { item }, "query", new FakeComponent());

            Assert.HasCount(1, rows);
            Assert.AreEqual("File", rows[0].ResultKind);
            Assert.AreEqual(tempFile, rows[0].FullPath);
            Assert.IsTrue(rows[0].IsFullSearchFileResult, "full-window file rows must be marked so the type filter can exclude them");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    private sealed class FakeComponent : IPluginComponent
    {
    }

    [TestMethod]
    public void SanitizeSingleLine_NullOrEmpty_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, PluginSearchResultMapper.SanitizeSingleLine(null));
        Assert.AreEqual(string.Empty, PluginSearchResultMapper.SanitizeSingleLine(string.Empty));
    }

    [TestMethod]
    public void SanitizeSingleLine_ReplacesNewlinesWithSpaces()
    {
        var input = "Line1\r\nLine2\nLine3\rLine4";
        var result = PluginSearchResultMapper.SanitizeSingleLine(input);

        Assert.AreEqual("Line1 Line2 Line3 Line4", result);
    }

    [TestMethod]
    public void SanitizeSingleLine_NoNewlines_ReturnsOriginal()
    {
        var input = "Single Line Text";
        var result = PluginSearchResultMapper.SanitizeSingleLine(input);

        Assert.AreEqual("Single Line Text", result);
    }
}
