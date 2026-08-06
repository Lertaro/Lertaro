using System.Windows;
using Lertaro.App.ViewModels.Service;
using Lertaro.Core.Indexer.Usn;

namespace Lertaro.App.Tests.ViewModels.Service;

[TestClass]
public sealed class SearchServiceStatusPresenterTests
{
    private sealed class Recorder
    {
        public bool? SearchBoxEnabled;
        public Visibility? LoadingPanelVisibility;
        public Visibility? ProgressBarVisibility;
        public bool? ProgressIndeterminate;
        public double? LoadingProgress;
        public Visibility? ErrorIconVisibility;
        public string? LoadingTitle;
        public string? LoadingStats;
        public Visibility? InstallButtonVisibility;
        public int ReadyCount;

        public SearchServiceStatusPresenter Build() => new(
            v => SearchBoxEnabled = v,
            v => LoadingPanelVisibility = v,
            v => ProgressBarVisibility = v,
            v => ProgressIndeterminate = v,
            v => LoadingProgress = v,
            v => ErrorIconVisibility = v,
            v => LoadingTitle = v,
            v => LoadingStats = v,
            v => InstallButtonVisibility = v,
            () => ReadyCount++);
    }

    [TestMethod]
    public void ShowConnecting_NotWaitingForReconnect_ShowsConnectingText()
    {
        var recorder = new Recorder();
        var presenter = recorder.Build();

        presenter.ShowConnecting(waitingForReconnect: false);

        Assert.AreEqual(Visibility.Visible, recorder.LoadingPanelVisibility);
        Assert.AreEqual(Visibility.Visible, recorder.ProgressBarVisibility);
        Assert.IsTrue(recorder.ProgressIndeterminate);
        Assert.AreEqual(Visibility.Collapsed, recorder.InstallButtonVisibility);
        Assert.AreEqual(Visibility.Collapsed, recorder.ErrorIconVisibility);
        Assert.IsFalse(recorder.SearchBoxEnabled);
        Assert.AreEqual("[Service_ConnectingState]", recorder.LoadingTitle);
        Assert.AreEqual("[Service_ConnectingDetail]", recorder.LoadingStats);
    }

    [TestMethod]
    public void ShowConnecting_WaitingForReconnect_ShowsWaitingText()
    {
        var recorder = new Recorder();
        var presenter = recorder.Build();

        presenter.ShowConnecting(waitingForReconnect: true);

        Assert.AreEqual("[Service_WaitingStart]", recorder.LoadingTitle);
        Assert.AreEqual("[Service_StartedDetail]", recorder.LoadingStats);
    }

    [TestMethod]
    public void ShowReconnecting_UsesWaitingTextAndIndeterminateProgress()
    {
        var recorder = new Recorder();
        var presenter = recorder.Build();

        presenter.ShowReconnecting();

        Assert.AreEqual("[Service_WaitingStart]", recorder.LoadingTitle);
        Assert.AreEqual("[Service_StartedDetail]", recorder.LoadingStats);
        Assert.IsTrue(recorder.ProgressIndeterminate);
    }

    [TestMethod]
    public void ProcessStatus_ReconnectingState_DelegatesToShowReconnecting()
    {
        var recorder = new Recorder();
        var presenter = recorder.Build();

        presenter.ProcessStatus(new UsnIndexer.IndexerStatus { State = "reconnecting" });

        Assert.AreEqual("[Service_WaitingStart]", recorder.LoadingTitle);
    }

    [TestMethod]
    public void ProcessStatus_LoadingCacheState_ShowsIndeterminateLoadingWithDiskIndexText()
    {
        var recorder = new Recorder();
        var presenter = recorder.Build();

        presenter.ProcessStatus(new UsnIndexer.IndexerStatus { State = "loading-cache" });

        Assert.AreEqual("[Service_LoadingDiskIndex]", recorder.LoadingTitle);
        Assert.AreEqual("[Service_LoadingDiskIndexDetail]", recorder.LoadingStats);
        Assert.IsTrue(recorder.ProgressIndeterminate);
    }

    [TestMethod]
    public void ProcessStatus_IndexingState_ShowsDeterminateProgressWithStats()
    {
        var recorder = new Recorder();
        var presenter = recorder.Build();

        presenter.ProcessStatus(new UsnIndexer.IndexerStatus { State = "indexing", Progress = 42, TotalFiles = 100, TotalDirs = 10 });

        Assert.IsFalse(recorder.ProgressIndeterminate);
        Assert.AreEqual(42, recorder.LoadingProgress);
        Assert.IsFalse(recorder.SearchBoxEnabled);
    }

    [TestMethod]
    public void ProcessStatus_ReadyState_HidesLoadingPanelEnablesSearchAndFiresOnReady()
    {
        var recorder = new Recorder();
        var presenter = recorder.Build();

        presenter.ProcessStatus(new UsnIndexer.IndexerStatus { State = "ready" });

        Assert.AreEqual(Visibility.Collapsed, recorder.LoadingPanelVisibility);
        Assert.IsTrue(recorder.SearchBoxEnabled);
        Assert.AreEqual(1, recorder.ReadyCount);
    }

    [TestMethod]
    public void ProcessStatus_UnknownState_DoesNothing()
    {
        var recorder = new Recorder();
        var presenter = recorder.Build();

        presenter.ProcessStatus(new UsnIndexer.IndexerStatus { State = "some-unknown-state" });

        Assert.IsNull(recorder.LoadingPanelVisibility);
        Assert.AreEqual(0, recorder.ReadyCount);
    }
}
