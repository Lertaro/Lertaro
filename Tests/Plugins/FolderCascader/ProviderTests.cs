using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.FolderCascader.Navigation;

namespace Lertaro.Plugins.FolderCascader.Tests;

[TestClass]
public sealed class ProviderTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    [TestMethod]
    public void AllocateHandle_ThenTryGetPath_RoundTrips()
    {
        var provider = new Provider();

        var handle = provider.AllocateHandle(@"C:\some\path");

        var found = provider.TryGetPath(handle, out var path);
        Assert.IsTrue(found);
        Assert.AreEqual(@"C:\some\path", path);
    }

    [TestMethod]
    public void AllocateHandle_CalledTwice_ReturnsDistinctHandles()
    {
        var provider = new Provider();

        var a = provider.AllocateHandle(@"C:\a");
        var b = provider.AllocateHandle(@"C:\b");

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void TryGetPath_UnknownHandle_ReturnsFalse()
    {
        var provider = new Provider();

        var found = provider.TryGetPath(new IntPtr(999), out var path);

        Assert.IsFalse(found);
        Assert.IsNull(path);
    }

    [TestMethod]
    public void AllocateCommand_CalledTwice_ReturnsDistinctIds()
    {
        var provider = new Provider();

        var a = provider.AllocateCommand(@"C:\a.txt");
        var b = provider.AllocateCommand(@"C:\b.txt");

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void ClearSession_ForgetsPreviouslyAllocatedHandles()
    {
        var provider = new Provider();
        var handle = provider.AllocateHandle(@"C:\a");

        provider.ClearSession();

        Assert.IsFalse(provider.TryGetPath(handle, out _));
    }

    [TestMethod]
    public void ClearSession_ResetsHandleCounterToStartOver()
    {
        var provider = new Provider();
        var first = provider.AllocateHandle(@"C:\a");

        provider.ClearSession();
        var afterClear = provider.AllocateHandle(@"C:\b");

        Assert.AreEqual(first, afterClear); // both are the first handle allocated after a fresh/cleared session
    }

    [TestMethod]
    public void CanProvide_EmptyFullPath_ReturnsFalse() =>
        Assert.IsFalse(new Provider().CanProvide(new FakeResult { FullPath = "" }));

    [TestMethod]
    public void CanProvide_NonEmptyFullPath_ReturnsTrue() =>
        Assert.IsTrue(new Provider().CanProvide(new FakeResult { FullPath = @"C:\a" }));

    [TestMethod]
    public void CanProvide_NullResult_ReturnsFalse() =>
        Assert.IsFalse(new Provider().CanProvide(null!));
}
