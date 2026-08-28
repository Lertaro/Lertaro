using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.ContentSearch.Providers;

namespace Lertaro.Plugins.ContentSearch.Tests.Providers;

[TestClass]
[DoNotParallelize]
public sealed class ContentSearchInstantProviderTests
{
    [TestInitialize]
    public void SetUp() => FuzzyMatchService.GetHighlightMaskFunc = (text, query) =>
                                {
                                    var mask = new bool[text.Length];
                                    var idx = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                                    if (idx >= 0)
                                    {
                                        for (var i = 0; i < query.Length && idx + i < mask.Length; i++)
                                            mask[idx + i] = true;
                                    }
                                    return mask;
                                };

    [TestCleanup]
    public void TearDown() => FuzzyMatchService.GetHighlightMaskFunc = null;

    [TestMethod]
    public void GetHighlightMask_StripsTriggerPrefix()
    {
        var provider = new ContentSearchInstantProvider();
        var mask = provider.GetHighlightMask("Hello world test", "c world");

        Assert.IsNotNull(mask);
        Assert.HasCount(16, mask);
        Assert.IsFalse(mask[0]);
        Assert.IsTrue(mask[6]);
        Assert.IsTrue(mask[7]);
        Assert.IsTrue(mask[8]);
        Assert.IsTrue(mask[9]);
        Assert.IsTrue(mask[10]);
    }

    [TestMethod]
    public void GetHighlightMask_TriggerOnly_ReturnsEmptyMaskWithoutHighlighting()
    {
        var provider = new ContentSearchInstantProvider();

        var maskAlone = provider.GetHighlightMask("已索引 8036 个文件 · 输入关键词搜索文件正文", "c");
        Assert.IsNotNull(maskAlone);
        Assert.IsFalse(maskAlone.Any(b => b));

        var maskWithSpace = provider.GetHighlightMask("已索引 8036 个文件 · 输入关键词搜索文件正文", "c ");
        Assert.IsNotNull(maskWithSpace);
        Assert.IsFalse(maskWithSpace.Any(b => b));
    }

    [TestMethod]
    public void GetHighlightMask_NonMatchingTrigger_ReturnsNull()
    {
        var provider = new ContentSearchInstantProvider();
        var mask = provider.GetHighlightMask("Hello world", "x world");
        Assert.IsNull(mask);
    }
}
