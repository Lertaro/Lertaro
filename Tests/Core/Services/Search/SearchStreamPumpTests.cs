using Lertaro.Core;
using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Search;

[TestClass]
public sealed class SearchStreamPumpTests
{
    [TestMethod]
    public void ResultChannel_IsBoundedToPreventSlowClientsAccumulatingResults()
    {
        var channel = SearchStreamPump.CreateResultChannel();

        for (var i = 0; i < SearchStreamPump.ResultBufferCapacity; i++)
            Assert.IsTrue(channel.Writer.TryWrite(new SearchResult()));

        Assert.IsFalse(channel.Writer.TryWrite(new SearchResult()));
    }
}
