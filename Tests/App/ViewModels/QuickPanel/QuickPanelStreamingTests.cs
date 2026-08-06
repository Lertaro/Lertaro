using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.QuickPanel;

// Nothing waits on anything else. A source on a disconnected share used to hold the whole summon open
// behind it; these pin the properties that replaced that -- the panel opens on the first arrival, and
// what arrives late still lands where the settings put it rather than where it finished.
[TestClass]
public sealed class QuickPanelStreamingTests
{
    private static SearchResult Entry(string folder, string name) => new()
    {
        Name = name,
        Path = System.IO.Path.Combine(folder, name),
        Metadata = new PluginSdk.Abstractions.FileMetadata(
            0, new DateTime(2026, 1, 1), new DateTime(2026, 1, 1), new DateTime(2026, 1, 1)),
    };

    // One gate per source, so a test decides the order things finish in rather than hoping for one.
    private sealed class Gates
    {
        private readonly Dictionary<string, TaskCompletionSource<List<SearchResult>>> _gates = new();

        public TaskCompletionSource<List<SearchResult>> For(string sourceId)
        {
            if (!_gates.TryGetValue(sourceId, out var gate))
                _gates[sourceId] = gate = new TaskCompletionSource<List<SearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
            return gate;
        }

        public void Deliver(string sourceId, string path, params string[] names)
            => For(sourceId).TrySetResult(names.Select(n => Entry(path, n)).ToList());

        public void DeliverNothing(string sourceId) => For(sourceId).TrySetResult(new List<SearchResult>());

        public Task<List<SearchResult>> Load(QuickPanelFolderSource source, CancellationToken _) => For(source.Id).Task;
    }

    private static QuickPanelFolderSource Folder(string id) => new() { Id = id, Path = @"C:\" + id };

    private static QuickPanelTab Workspace(string id, params string[] sourceIds)
    {
        var tab = new QuickPanelTab { Id = id, Name = id };
        foreach (var sourceId in sourceIds) tab.Folders.Add(Folder(sourceId));
        return tab;
    }

    private static (QuickPanelViewModel Vm, Gates Gates) Build(QuickPanelSettings settings)
    {
        var gates = new Gates();
        return (new QuickPanelViewModel(() => settings, gates.Load, saveSettings: () => { }), gates);
    }

    // The whole point: the panel is ready to open while a source is still outstanding.
    [TestMethod]
    public async Task Refresh_ReturnsOnTheFirstArrival_NotTheLast()
    {
        var settings = new QuickPanelSettings
        {
            Tabs = new List<QuickPanelTab> { Workspace("w1", "fast", "slow") },
            ActiveTabId = "w1",
        };
        var (vm, gates) = Build(settings);

        var refresh = vm.RefreshAsync();
        Assert.IsFalse(refresh.IsCompleted, "nothing has arrived yet");

        gates.Deliver("fast", @"C:\fast", "a.txt");
        await refresh;

        Assert.IsTrue(vm.HasContent, "the panel can open on what has landed");
        CollectionAssert.AreEqual(new[] { "fast" }, vm.Groups.Select(g => g.SourceId).ToList());

        // And the slow one still lands, into a panel that is already up.
        gates.Deliver("slow", @"C:\slow", "b.txt");
        await Task.Delay(50);
        CollectionAssert.AreEqual(new[] { "fast", "slow" }, vm.Groups.Select(g => g.SourceId).ToList());
    }

    // Arrival order is a race; the configured order is not. A late source takes the place the settings
    // give it, not the end of the list.
    [TestMethod]
    public async Task ALateGroup_LandsWhereTheSettingsPutIt_NotWhereItFinished()
    {
        var settings = new QuickPanelSettings
        {
            Tabs = new List<QuickPanelTab> { Workspace("w1", "first", "second", "third") },
            ActiveTabId = "w1",
        };
        var (vm, gates) = Build(settings);

        var refresh = vm.RefreshAsync();
        gates.Deliver("third", @"C:\third", "c.txt");
        await refresh;

        gates.Deliver("first", @"C:\first", "a.txt");
        gates.Deliver("second", @"C:\second", "b.txt");
        await Task.Delay(50);

        CollectionAssert.AreEqual(new[] { "first", "second", "third" }, vm.Groups.Select(g => g.SourceId).ToList());
    }

    [TestMethod]
    public async Task ALateWorkspace_GetsItsTabInTheConfiguredOrder()
    {
        var settings = new QuickPanelSettings
        {
            Tabs = new List<QuickPanelTab> { Workspace("w1", "s1"), Workspace("w2", "s2"), Workspace("w3", "s3") },
            ActiveTabId = "w1",
        };
        var (vm, gates) = Build(settings);

        var refresh = vm.RefreshAsync();
        gates.Deliver("s3", @"C:\s3", "c.txt");
        await refresh;

        gates.Deliver("s2", @"C:\s2", "b.txt");
        gates.Deliver("s1", @"C:\s1", "a.txt");
        await Task.Delay(50);

        CollectionAssert.AreEqual(new[] { "w1", "w2", "w3" }, vm.Tabs.Select(t => t.Id).ToList());
    }

    // The workspace the panel wants may be slower than another. It shows what it has meanwhile, and
    // switches the moment the wanted one turns up.
    [TestMethod]
    public async Task TheWantedWorkspace_TakesOverWhenItArrivesLate()
    {
        var settings = new QuickPanelSettings
        {
            Tabs = new List<QuickPanelTab> { Workspace("w1", "s1"), Workspace("w2", "s2") },
            ActiveTabId = "w2",
        };
        var (vm, gates) = Build(settings);

        var refresh = vm.RefreshAsync();
        gates.Deliver("s1", @"C:\s1", "a.txt");
        await refresh;

        Assert.AreEqual("w1", vm.Tabs.Single(t => t.IsSelected).Id, "it shows what it has");

        gates.Deliver("s2", @"C:\s2", "b.txt");
        await Task.Delay(50);

        Assert.AreEqual("w2", vm.Tabs.Single(t => t.IsSelected).Id, "and hands over when the wanted one lands");
        CollectionAssert.AreEqual(new[] { "s2" }, vm.Groups.Select(g => g.SourceId).ToList());
    }

    // A summon where every source comes back empty has to finish, or the manager would wait forever for
    // a first arrival that is never coming.
    [TestMethod]
    public async Task EverythingEmpty_StillFinishes()
    {
        var settings = new QuickPanelSettings
        {
            Tabs = new List<QuickPanelTab> { Workspace("w1", "s1"), Workspace("w2", "s2") },
            ActiveTabId = "w1",
        };
        var (vm, gates) = Build(settings);

        var refresh = vm.RefreshAsync();
        gates.DeliverNothing("s1");
        gates.DeliverNothing("s2");
        await refresh;

        Assert.IsFalse(vm.HasContent);
        Assert.IsEmpty(vm.Tabs);
    }

    // One source failing must not take the summon with it, now that the failure happens on its own task.
    [TestMethod]
    public async Task ASourceThatThrows_DoesNotStopTheRest()
    {
        var settings = new QuickPanelSettings
        {
            Tabs = new List<QuickPanelTab> { Workspace("w1", "bad", "good") },
            ActiveTabId = "w1",
        };
        var (vm, gates) = Build(settings);

        var refresh = vm.RefreshAsync();
        gates.For("bad").TrySetException(new UnauthorizedAccessException("no"));
        gates.Deliver("good", @"C:\good", "a.txt");
        await refresh;

        CollectionAssert.AreEqual(new[] { "good" }, vm.Groups.Select(g => g.SourceId).ToList());
    }
}
