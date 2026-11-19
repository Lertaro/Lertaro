using System.Windows.Threading;
using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.DynamicSidebar;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search.DynamicSidebar;

[STATestClass]
[DoNotParallelize]
public sealed class SearchSidebarCountHelperTests
{
    [TestMethod]
    public void Update_CountsOnWorkerAndPublishesTheLatestBatchOnDispatcher()
    {
        using var vm = new SearchViewModel();
        var dispatcher = Dispatcher.CurrentDispatcher;
        var caller = Environment.CurrentManagedThreadId;
        var evaluationThread = caller;
        var publicationThread = -1;
        var group = new DynamicSidebarGroupViewModel(new SidebarFilterGroup
        {
            Id = "Type", Items = [new SidebarFilterItem
            {
                Id = "Folder", MatchPredicate = row =>
                {
                    evaluationThread = Environment.CurrentManagedThreadId;
                    return row.IsDir;
                }
            }]
        }, vm);
        group.Items[0].PropertyChanged += (_, _) => publicationThread = Environment.CurrentManagedThreadId;
        using var helper = new SearchSidebarCountHelper([group]);

        helper.Update([new AppSearchResult { IsDir = true }], true);
        helper.Update([new AppSearchResult { IsDir = false }, new AppSearchResult { IsDir = true }], true);
        DrainUntilCompleted(helper.PendingUpdate, dispatcher);

        Assert.AreEqual(2, group.Items[0].Count);
        Assert.AreNotEqual(caller, evaluationThread);
        Assert.AreEqual(caller, publicationThread);

        helper.Reset();
        helper.Update([new AppSearchResult { IsDir = false }], true);
        DrainUntilCompleted(helper.PendingUpdate, dispatcher);
        Assert.AreEqual(0, group.Items[0].Count);
    }

    private static void DrainUntilCompleted(Task work, Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        _ = work.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        work.GetAwaiter().GetResult();
    }
}
