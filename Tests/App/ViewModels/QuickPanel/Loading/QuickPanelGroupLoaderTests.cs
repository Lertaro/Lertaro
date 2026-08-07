using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.App.ViewModels.QuickPanel.Loading;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.QuickPanel.Loading;

[TestClass]
public sealed class QuickPanelGroupLoaderTests
{
    [TestMethod]
    public async Task LoadAsync_PublishesArrivalBatchBeforeApplyingFinalSort()
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appeared = new TaskCompletionSource<QuickPanelGroupViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loader = new QuickPanelGroupLoader(async (_, progress, _) =>
        {
            progress.Report(new[] { Entry("z"), Entry("a") });
            await finished.Task;
            return new List<SearchResult> { Entry("z"), Entry("a") };
        }, mapOnBackground: false);
        var workspace = new QuickPanelTab { Id = "workspace" };
        var source = new QuickPanelFolderSource { Id = "source", Path = @"C:\source", Kind = QuickPanelSourceKind.All, SortByModified = false };

        var loading = loader.LoadAsync(workspace, source, group => appeared.TrySetResult(group), CancellationToken.None);
        var group = await appeared.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsFalse(loading.IsCompleted, "the first batch makes the group available before enumeration finishes");

        finished.SetResult();
        await loading;

        CollectionAssert.AreEqual(new[] { "a", "z" }, group.Items.Select(item => item.Name).ToList());
    }

    private static SearchResult Entry(string name) => new()
    {
        Name = name,
        Path = @"C:\source\" + name,
        Metadata = new PluginSdk.Abstractions.FileMetadata(0, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue),
    };
}
