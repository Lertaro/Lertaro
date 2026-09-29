using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.PluginSdk.Tests.Abstractions;

// The full search window now asks content-style providers for their rows one at a time. That member has a
// default body, so a provider written before it existed -- in this repo, or a third-party plugin already
// shipped -- keeps answering without recompiling against anything new. These tests keep that promise
// honest, and pin the one thing that would silently break it: if GetFileResults ever gets redirected back
// through the streamed member, the two call each other forever.
[TestClass]
public sealed class FullSearchFileResultProviderDefaultsTests
{
    private sealed class ListOnlyProvider : IFullSearchFileResultProvider
    {
        public int Calls;

        public IReadOnlyList<InstantResultItem> GetFileResults(string query, int limit)
        {
            Calls++;
            return Enumerable.Range(0, 3)
                .Select(i => new InstantResultItem { Title = $"hit {i}", ActionArgument = $@"C:\Docs\{i}.txt" })
                .ToList();
        }
    }

    [TestMethod]
    public void GetFileResultsStreamed_DefaultBody_WalksTheListAnswer()
    {
        IFullSearchFileResultProvider provider = new ListOnlyProvider();

        var streamed = provider.GetFileResultsStreamed("cs report", 10).ToList();

        Assert.HasCount(3, streamed);
        Assert.AreEqual(@"C:\Docs\0.txt", streamed[0].ActionArgument);
        Assert.AreEqual(@"C:\Docs\2.txt", streamed[2].ActionArgument);
    }

    [TestMethod]
    public void GetFileResultsStreamed_DefaultBody_DoesNotAnswerBeforeItIsEnumerated()
    {
        // The host starts the walk on a background thread and may abandon it when the user types again, so
        // building the whole answer just to be handed an enumerator would put the cost right back.
        var provider = new ListOnlyProvider();
        // Through the interface, which is the only way a default implementation is reachable -- and how
        // the host calls it: PluginManager hands out IFullSearchFileResultProvider, never a concrete type.
        IFullSearchFileResultProvider streamed = provider;

        var walk = streamed.GetFileResultsStreamed("cs report", 7);
        Assert.AreEqual(0, provider.Calls, "creating the iterator must not call the provider");

        Assert.AreEqual(3, walk.Count());
        Assert.AreEqual(1, provider.Calls, "and must not call it more than once per enumeration");
    }
}
