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
        var mask = provider.GetHighlightMask("Hello world test", "cs world");

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
    public void GetHighlightMask_TriggerWithSpaceOnly_ReturnsEmptyMaskWithoutHighlighting()
    {
        var provider = new ContentSearchInstantProvider();

        var maskAlone = provider.GetHighlightMask("已索引 8036 个文件 · 输入关键词搜索文件正文", "cs");
        Assert.IsNull(maskAlone);

        var maskWithSpace = provider.GetHighlightMask("已索引 8036 个文件 · 输入关键词搜索文件正文", "cs ");
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

    // Providers are handed the untouched box text so they can still recognise their own word -- and with it
    // the host's trailing ":token" syntax, which used to go straight into the full-text query: "cs world
    // :jpg" asked the index for a document containing ":jpg". The tokens come off via the host's own
    // stripper before the term is used, which is what this pins.
    [TestMethod]
    public void GetHighlightMask_TokenSuffixIsTakenOffTheSearchedTerm()
    {
        var wired = SearchQueryService.StripQueryTokensFunc;
        SearchQueryService.StripQueryTokensFunc = q => q.Replace(" :jpg", string.Empty, StringComparison.Ordinal);
        try
        {
            var mask = new ContentSearchInstantProvider().GetHighlightMask("Hello world test", "cs world :jpg");

            Assert.IsNotNull(mask);
            Assert.IsTrue(mask[6], "the term still highlights, so the token was not searched for");
            Assert.IsFalse(mask[12]);
        }
        finally
        {
            SearchQueryService.StripQueryTokensFunc = wired;
        }
    }

    // A full-width space is the other separator no copy of this rule used to accept.
    [TestMethod]
    public void GetHighlightMask_FullWidthSeparator_MasksTheTerm()
    {
        var mask = new ContentSearchInstantProvider().GetHighlightMask("Hello world test", "cs　world");

        Assert.IsNotNull(mask);
        Assert.IsTrue(mask[6]);
    }
}
