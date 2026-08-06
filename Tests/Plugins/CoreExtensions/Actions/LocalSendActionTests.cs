using Lertaro.Plugins.CoreExtensions.Actions;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Actions;

[TestClass]
public class LocalSendActionTests
{
    [TestMethod]
    public void CanExecute_EmptyList_ReturnsFalse()
    {
        var action = new LocalSendAction();
        var canExec = action.CanExecute(Array.Empty<ISearchResult>());
        Assert.IsFalse(canExec);
    }

    [TestMethod]
    public void DisplayName_IsNotEmpty()
    {
        var action = new LocalSendAction();
        Assert.IsFalse(string.IsNullOrWhiteSpace(action.DisplayName));
    }
}
