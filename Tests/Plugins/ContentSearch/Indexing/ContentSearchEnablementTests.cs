using Lertaro.Plugins.ContentSearch.Indexing;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

[TestClass]
public sealed class ContentSearchEnablementTests
{
    [TestMethod]
    public void IsRuntimeEnabled_StaysEnabledWhileEitherProviderIsEnabled()
    {
        var enabledComponents = new HashSet<string>();
        bool IsEnabled(string dll, string type, string name) => enabledComponents.Contains(type);

        Assert.IsFalse(ContentSearchEnablement.IsRuntimeEnabled(IsEnabled, "ContentSearch.dll"));

        enabledComponents.Add("InstantProvider");
        Assert.IsTrue(ContentSearchEnablement.IsRuntimeEnabled(IsEnabled, "ContentSearch.dll"));

        enabledComponents.Clear();
        enabledComponents.Add("FullSearchFileResultProvider");
        Assert.IsTrue(ContentSearchEnablement.IsRuntimeEnabled(IsEnabled, "ContentSearch.dll"));
    }
}
