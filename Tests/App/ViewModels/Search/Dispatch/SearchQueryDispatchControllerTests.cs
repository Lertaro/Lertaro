using Lertaro.App.ViewModels.Search.Dispatch;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search.Dispatch;

[TestClass]
public sealed class SearchQueryDispatchControllerTests
{
    [TestMethod]
    public void StreamFullSearchFileRows_CurrentQuery_PreservesBatchesAndProviderIdentity()
    {
        var first = new TestProvider("first", 301);
        var second = new TestProvider("second", 1);
        var batches = new List<List<AppSearchResult>>();

        SearchQueryDispatchController.StreamFullSearchFileRows("cs report", [first, second], () => true, batches.Add);

        CollectionAssert.AreEqual(new[] { 60, 240, 1, 1 }, batches.Select(b => b.Count).ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(0, 301).Select(i => $"first-{i}").ToArray(),
            batches.Take(3).SelectMany(b => b).Select(r => r.Name).ToArray());
        Assert.IsTrue(batches.Take(3).SelectMany(b => b).All(r => ReferenceEquals(first, r.SourceProvider) && r.IsFullSearchFileResult));
        Assert.AreSame(second, batches[3][0].SourceProvider);
        Assert.AreEqual("second-0", batches[3][0].Name);
        Assert.IsTrue(first.Disposed);
        Assert.IsTrue(second.Disposed);
    }

    [TestMethod]
    public void StreamFullSearchFileRows_AlreadySuperseded_DoesNotReadProvider()
    {
        var provider = new TestProvider("old", 100);
        var batches = new List<List<AppSearchResult>>();

        SearchQueryDispatchController.StreamFullSearchFileRows("cs a", [provider], () => false, batches.Add);

        Assert.AreEqual(0, provider.Reads);
        Assert.IsEmpty(batches);
    }

    [TestMethod]
    public void StreamFullSearchFileRows_SupersededDuringRead_StopsAndDisposesWithoutFlushing()
    {
        var current = true;
        var first = new TestProvider("old", 100) { BeforeYield = i => { if (i == 5) current = false; } };
        var second = new TestProvider("unread", 1);
        var batches = new List<List<AppSearchResult>>();

        SearchQueryDispatchController.StreamFullSearchFileRows("cs a", [first, second], () => current, batches.Add);

        Assert.AreEqual(6, first.Reads);
        Assert.IsTrue(first.Disposed);
        Assert.AreEqual(0, second.Reads);
        Assert.IsEmpty(batches);
    }

    [TestMethod]
    public void StreamFullSearchFileRows_SupersededAfterFirstBatch_DoesNotStartAnotherRead()
    {
        var current = true;
        var provider = new TestProvider("old", 100);
        var batches = new List<List<AppSearchResult>>();

        SearchQueryDispatchController.StreamFullSearchFileRows("cs a", [provider], () => current,
            rows => { batches.Add(rows); current = false; });

        Assert.HasCount(60, Assert.ContainsSingle(batches));
        Assert.AreEqual(60, provider.Reads);
        Assert.IsTrue(provider.Disposed);
    }

    [TestMethod]
    public void StreamFullSearchFileRows_SupersededAtEnd_DropsThePendingTail()
    {
        var current = true;
        var provider = new TestProvider("old", 5) { OnCompleted = () => current = false };
        var batches = new List<List<AppSearchResult>>();

        SearchQueryDispatchController.StreamFullSearchFileRows("cs a", [provider], () => current, batches.Add);

        Assert.AreEqual(5, provider.Reads);
        Assert.IsTrue(provider.Disposed);
        Assert.IsEmpty(batches);
    }

    [TestMethod]
    public void StreamFullSearchFileRows_SupersededWhileFlushing_DoesNotInvokeTheNextProvider()
    {
        var current = true;
        var first = new TestProvider("first", 1);
        var second = new TestProvider("unread", 1);
        var batches = new List<List<AppSearchResult>>();

        SearchQueryDispatchController.StreamFullSearchFileRows("cs a", [first, second], () => current,
            rows => { batches.Add(rows); current = false; });

        Assert.HasCount(1, Assert.ContainsSingle(batches));
        Assert.AreEqual(0, second.StreamsStarted, "A provider can start expensive work before returning its iterator.");
    }

    private sealed class TestProvider(string name, int count) : IFullSearchFileResultProvider
    {
        public string Name => name;
        public int Reads { get; private set; }
        public int StreamsStarted { get; private set; }
        public bool Disposed { get; private set; }
        public Action<int>? BeforeYield { get; init; }
        public Action? OnCompleted { get; init; }

        public IReadOnlyList<InstantResultItem> GetFileResults(string query, int limit) =>
            throw new InvalidOperationException("The streaming path must not collect the provider's entire answer.");

        public IEnumerable<InstantResultItem> GetFileResultsStreamed(string query, int limit)
        {
            StreamsStarted++;
            return Enumerate();
        }

        private IEnumerable<InstantResultItem> Enumerate()
        {
            try
            {
                for (var i = 0; i < count; i++)
                {
                    Reads++;
                    BeforeYield?.Invoke(i);
                    // An opaque icon path avoids both vector rendering and filesystem probes during mapping.
                    yield return new InstantResultItem { Title = $"{name}-{i}", ActionType = "None", IconData = "path:unused.ico" };
                }
                OnCompleted?.Invoke();
            }
            finally
            {
                Disposed = true;
            }
        }
    }
}
