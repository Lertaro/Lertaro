using Lertaro.Core.Hook;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.Core.Tests.Hook;

[TestClass]
public sealed class FileDialogNavigationTrackerTests
{
    private static readonly IntPtr Dialog = new(0x1000);

    private sealed class RecordingAdapter : IFileDialogAdapter
    {
        public List<(IntPtr Hwnd, string Path)> Navigations { get; } = [];
        public bool CanHandle(IntPtr hwnd, string className, string processName) => true;
        public string? GetCurrentPath(IntPtr hwnd) => null;
        public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect) { rect = default; return false; }
        public bool RestoreFocus(IntPtr hwnd) => true;
        public bool NavigateTo(IntPtr hwnd, string targetPath)
        {
            Navigations.Add((hwnd, targetPath));
            return true;
        }
    }

    [TestMethod]
    public async Task FirstSighting_DoesNotReadOrNavigateProvider()
    {
        var tracker = new FileDialogNavigationTracker();
        tracker.SetLastActiveExplorerPath(@"C:\Old");
        var adapter = new RecordingAdapter();
        await tracker.HandleDialogSeenAsync(Dialog, adapter, true, () => throw new AssertFailedException("No read on first sighting"));
        Assert.IsEmpty(adapter.Navigations);
    }

    [TestMethod]
    public async Task ReturningFromProvider_UsesFreshPathEvenWithoutAPoll()
    {
        var tracker = new FileDialogNavigationTracker();
        tracker.SetLastActiveExplorerPath(@"C:\Old");
        var adapter = new RecordingAdapter();
        await tracker.HandleDialogSeenAsync(Dialog, adapter, false);
        await tracker.HandleDialogSeenAsync(Dialog, adapter, true, () => @"D:\Current");
        Assert.AreEqual((Dialog, @"D:\Current"), Assert.ContainsSingle(adapter.Navigations));
    }

    [TestMethod]
    public async Task UnchangedProviderPath_ThenNonProviderActivation_LeavesDialogAlone()
    {
        var tracker = new FileDialogNavigationTracker();
        tracker.SetLastActiveExplorerPath(@"C:\Old");
        var adapter = new RecordingAdapter();
        await tracker.HandleDialogSeenAsync(Dialog, adapter, false);
        tracker.SetLastActiveExplorerPath(@"c:\OLD");
        await tracker.HandleDialogSeenAsync(Dialog, adapter, false, () => throw new AssertFailedException("Must not read an unrelated host"));
        Assert.IsEmpty(adapter.Navigations);
    }

    [TestMethod]
    public async Task ChangedProviderPath_FollowsOnceEvenWhenAnotherAppWasInBetween()
    {
        var tracker = new FileDialogNavigationTracker();
        var adapter = new RecordingAdapter();
        await tracker.HandleDialogSeenAsync(Dialog, adapter, false);
        tracker.SetLastActiveExplorerPath(@"D:\Changed");
        await tracker.HandleDialogSeenAsync(Dialog, adapter, false);
        await tracker.HandleDialogSeenAsync(Dialog, adapter, false);
        Assert.AreEqual((Dialog, @"D:\Changed"), Assert.ContainsSingle(adapter.Navigations));
    }

    [TestMethod]
    public async Task FailedFreshRead_PreservesLastKnownPath()
    {
        var tracker = new FileDialogNavigationTracker();
        tracker.SetLastActiveExplorerPath(@"C:\Known");
        var adapter = new RecordingAdapter();
        await tracker.HandleDialogSeenAsync(Dialog, adapter, false);
        await tracker.HandleDialogSeenAsync(Dialog, adapter, true, () => null);
        Assert.AreEqual((Dialog, @"C:\Known"), Assert.ContainsSingle(adapter.Navigations));
    }

    [TestMethod]
    public async Task SecondDialog_AndClearedTracker_AreFirstSightings()
    {
        var tracker = new FileDialogNavigationTracker();
        var adapter = new RecordingAdapter();
        await tracker.HandleDialogSeenAsync(Dialog, adapter, false);
        tracker.SetLastActiveExplorerPath(@"D:\Changed");
        await tracker.HandleDialogSeenAsync(new IntPtr(0x2000), adapter, true);
        tracker.Clear();
        tracker.SetLastActiveExplorerPath(@"C:\NewSession");
        await tracker.HandleDialogSeenAsync(Dialog, adapter, true);
        Assert.IsEmpty(adapter.Navigations);
    }
}
