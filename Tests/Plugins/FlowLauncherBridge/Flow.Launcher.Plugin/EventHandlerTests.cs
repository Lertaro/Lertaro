using Flow.Launcher.Plugin;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Flow.Launcher.Plugin;

[TestClass]
public sealed class EventHandlerTests
{
    [TestMethod]
    public void ResultUpdatedEventArgs_QueryAndResultsArePublicFields_ForPluginAbiCompatibility()
    {
        var queryField = typeof(ResultUpdatedEventArgs).GetField(nameof(ResultUpdatedEventArgs.Query));
        Assert.IsNotNull(queryField, "ResultUpdatedEventArgs.Query must be a public field for ABI compatibility with compiled Flow plugins.");

        var resultsField = typeof(ResultUpdatedEventArgs).GetField(nameof(ResultUpdatedEventArgs.Results));
        Assert.IsNotNull(resultsField, "ResultUpdatedEventArgs.Results must be a public field for ABI compatibility with compiled Flow plugins.");

        var tokenProp = typeof(ResultUpdatedEventArgs).GetProperty(nameof(ResultUpdatedEventArgs.Token));
        Assert.IsNotNull(tokenProp, "ResultUpdatedEventArgs.Token must be a public property for ABI compatibility with compiled Flow plugins.");

        var args = new ResultUpdatedEventArgs
        {
            Query = new Query { Search = "test" },
            Results = [new Result { Title = "Title" }],
            Token = CancellationToken.None
        };

        Assert.AreEqual("test", args.Query.Search);
        Assert.HasCount(1, args.Results);
        Assert.AreEqual(CancellationToken.None, args.Token);
    }
}
