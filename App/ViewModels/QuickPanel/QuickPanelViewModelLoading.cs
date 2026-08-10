using Lertaro.App.ViewModels.QuickPanel.Loading;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.QuickPanel;

// What a refresh actually loads. Split out of QuickPanelViewModel.cs purely to keep that file under the
// repo's per-file line limit; it has no state of its own and only ever operates on the one view model it
// is part of.
public partial class QuickPanelViewModel
{
    private static readonly TimeSpan PreferredTabExclusiveLoadWindow = TimeSpan.FromMilliseconds(200);
    private int _refreshGeneration;

    /// <summary>
    /// Loads the preferred tab first and returns as soon as there is something worth opening for, then
    /// fills the remaining tabs in the background.
    /// </summary>
    /// <remarks>
    /// The tab selected for this summon gets the loading resources to itself until its first group lands.
    /// Only then are the other tabs started in the background, after yielding so the caller can construct
    /// and paint the window first. A short grace period keeps a disconnected preferred source from
    /// holding the whole panel closed indefinitely.
    ///
    /// The task returned is deliberately not "everything is loaded": the caller's question is only
    /// whether to open a window, and that is answered by the first entry. The rest lands afterwards,
    /// into a panel that is already on screen.
    ///
    /// Each source now streams bounded arrival batches into a group so one large recursive folder cannot
    /// hold the whole panel closed. Those batches are only a provisional view: the source applies its
    /// complete sort and cap once enumeration ends, so the final list remains exactly the configured one.
    /// </remarks>
    public async Task RefreshAsync(string? processName = null, CancellationToken token = default)
    {
        var refreshGeneration = ++_refreshGeneration;
        // Each open starts unfiltered. The box is part of the window and every open builds a new one, so
        // it is empty on screen -- a query left on this view model would narrow the list by something
        // the user cannot see and did not type. Assigned to the field rather than the property: there is
        // nothing to re-filter, the groups it would run over are about to be replaced.
        _searchQuery = string.Empty;
        OnPropertyChanged(nameof(SearchQuery));

        var settings = _readSettings();
        // Disabled workspaces and closed plugin tabs are dropped here rather than filtered at every use:
        // a tab that isn't in the strip must also not be reachable by a process rule or the number keys.
        var workspaces = settings.Tabs.Where(tab => tab.Enabled).ToList();
        var candidates = OrderedTabs(settings, workspaces);

        _content = new Dictionary<string, List<QuickPanelGroupViewModel>>(StringComparer.OrdinalIgnoreCase);
        _tabs = new List<IQuickPanelTabSource>();
        _pendingTabs = candidates;
        // What the panel wants to open on, decided before anything has loaded. _activeTabId is what it
        // is actually showing, which may be a stand-in until the wanted one turns up -- or forever, if
        // that tab has nothing in it.
        _wantedTabId = ResolveActiveTabId(settings, processName, workspaces, candidates);
        _activeTabId = string.Empty;

        RebuildTabs();
        ShowActiveTab();

        // Completed by the first group to land. A summon that turns up empty still finishes through the
        // preferred load or the combined fallback completion below rather than through this.
        var firstArrival = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _firstArrival = firstArrival;

        var preferred = candidates.FirstOrDefault(tab =>
            tab.Id.Equals(_wantedTabId, StringComparison.OrdinalIgnoreCase)) ?? candidates.FirstOrDefault();
        if (preferred == null)
        {
            _firstArrival = null;
            return;
        }

        Task Load(IQuickPanelTabSource tab)
            => tab.LoadAsync((group, rank) =>
            {
                if (refreshGeneration == _refreshGeneration)
                    Place(tab, group, rank);
            }, token);

        var preferredLoad = Load(preferred);
        var remaining = candidates.Where(tab => !ReferenceEquals(tab, preferred)).ToList();
        var grace = Task.Delay(PreferredTabExclusiveLoadWindow, token);
        await Task.WhenAny(firstArrival.Task, preferredLoad, grace).ConfigureAwait(true);

        if (firstArrival.Task.IsCompleted)
        {
            _ = FinishInBackgroundAsync(preferredLoad, remaining);
            _firstArrival = null;
            return;
        }

        // ponytail: 200 ms is a fixed responsiveness ceiling, not a latency model. If source telemetry
        // is added later, replace it with an adaptive per-source threshold; until then it prevents a
        // disconnected preferred folder from undoing the panel's existing non-blocking behavior.
        var everything = Task.WhenAll(remaining.Select(Load).Prepend(preferredLoad));

        // Whichever comes first: something to show, or nothing left to wait for.
        await Task.WhenAny(firstArrival.Task, everything).ConfigureAwait(true);
        _ = ObserveBackgroundCompletionAsync(everything);
        _firstArrival = null;

        async Task FinishInBackgroundAsync(Task alreadyStarted, IEnumerable<IQuickPanelTabSource> later)
        {
            // Yield before even calling the remaining providers: some complete synchronously, and doing
            // that work inline on the UI context would move it back in front of the first window paint.
            // Without a context there is no pending frame to yield to, so starting inline preserves the
            // useful synchronous-completion behavior for headless callers.
            if (SynchronizationContext.Current != null)
                await Task.Yield();
            await ObserveBackgroundCompletionAsync(Task.WhenAll(later.Select(Load).Prepend(alreadyStarted)));
        }
    }

    private static async Task ObserveBackgroundCompletionAsync(Task completion)
    {
        try
        {
            await completion.ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // A newer refresh or caller cancellation makes the remaining tabs irrelevant.
        }
        catch (Exception ex)
        {
            Logger.Log($"[QuickPanel] Background tab load failed: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>Tabs still loading, in configured order -- what a tab's own position is read from.</summary>
    private List<IQuickPanelTabSource> _pendingTabs = new();

    private TaskCompletionSource? _firstArrival;

    /// <summary>Loads one workspace's visible folders, each on its own, and files each as it lands.</summary>
    internal async Task LoadWorkspaceAsync(
        QuickPanelTab workspace, Action<QuickPanelGroupViewModel, int> place, CancellationToken token)
    {
        var visible = QuickPanelGroupOrdering.Resolve(
            workspace.Folders.Select(folder => folder.Id),
            workspace.GroupOrder,
            workspace.DisabledGroupIds).ToList();

        await Task.WhenAll(visible.Select(async (id, rank) =>
        {
            if (workspace.Folders.FirstOrDefault(folder => folder.Id == id) is not { } folder) return;

            await _groupLoader.LoadAsync(workspace, folder, group => place(group, rank), token).ConfigureAwait(true);
        })).ConfigureAwait(true);
    }

    /// <summary>Files a finished group under its tab, in the position the settings give it.</summary>
    /// <remarks>
    /// At its configured rank, never appended. Groups now arrive in whatever order their sources happen
    /// to finish, so appending would let a fast source outrank a slow one and quietly replace the user's
    /// own order with a race -- the same trap the startup panel's tabs hit and solved the same way.
    ///
    /// The tab appears with the workspace's first group, for the same reason it disappears when a
    /// workspace has none: a tab is only worth a place in the strip if there is something behind it.
    /// </remarks>
    private void Place(IQuickPanelTabSource tab, QuickPanelGroupViewModel group, int rank)
    {
        // Sources finish on their own tasks, so two can land at the same moment. In the running app the
        // UI SynchronizationContext serialises the continuations and this is safe by accident of where
        // they resume; the lock is what makes it true without depending on that, and it is the only
        // thing holding these collections together anywhere there is no dispatcher.
        lock (_placing)
        {
            if (!_content.TryGetValue(tab.Id, out var groups))
            {
                _content[tab.Id] = groups = new List<QuickPanelGroupViewModel>();
                AddTab(tab);
            }

            var at = groups.FindIndex(existing => RankOf(existing.SourceId) > rank);
            if (at < 0) at = groups.Count;
            groups.Insert(at, group);
            _ranks[group.SourceId] = rank;

            if (tab.Id.Equals(_activeTabId, StringComparison.OrdinalIgnoreCase))
                ShowActiveTab();
        }

        _firstArrival?.TrySetResult();
    }

    private readonly object _placing = new();

    // Where each source sits in its workspace's configured order, remembered as it lands so the next
    // arrival can be slotted against it.
    private readonly Dictionary<string, int> _ranks = new(StringComparer.OrdinalIgnoreCase);

    private int RankOf(string sourceId) => _ranks.TryGetValue(sourceId, out var rank) ? rank : int.MaxValue;

    /// <summary>What the panel wants to be showing, which it may not be able to yet.</summary>
    private string _wantedTabId = string.Empty;

    /// <summary>Gives a tab its place in the strip, at the position the settings order it in.</summary>
    /// <remarks>
    /// Tabs arrive in whatever order they first produce something, so the position comes from the
    /// configured order rather than from arrival -- appending would let a fast tab outrank a slow one and
    /// quietly replace the user's own order with a race.
    ///
    /// The wanted tab may be slower than another, or may have nothing at all. Until it arrives the panel
    /// shows the first tab it has, and switches the moment the wanted one does turn up. The wanted id is
    /// never overwritten by that stand-in, which is what lets it still be honoured later.
    /// </remarks>
    private void AddTab(IQuickPanelTabSource tab)
    {
        var at = _pendingTabs.IndexOf(tab);
        var before = _tabs.FindIndex(existing => _pendingTabs.IndexOf(existing) > at);
        if (before < 0) before = _tabs.Count;
        _tabs.Insert(before, tab);

        var isWanted = tab.Id.Equals(_wantedTabId, StringComparison.OrdinalIgnoreCase);
        var showingNothing = string.IsNullOrEmpty(_activeTabId)
            || !_tabs.Any(existing => existing.Id.Equals(_activeTabId, StringComparison.OrdinalIgnoreCase));

        if (isWanted || showingNothing)
            _activeTabId = isWanted ? tab.Id : _tabs[0].Id;

        RebuildTabs();
        if (isWanted || showingNothing) ShowActiveTab();
    }

    /// <summary>The folder source behind a group, looked up across every workspace this refresh knows.</summary>
    private QuickPanelFolderSource? SourceOf(string sourceId) => _pendingTabs
        .OfType<WorkspaceTabSource>()
        .SelectMany(tab => tab.Workspace.Folders)
        .FirstOrDefault(folder => folder.Id.Equals(sourceId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Loads one group's source again and puts the result back into that same group.</summary>
    /// <remarks>
    /// For after a drop: the files were copied by the shell behind its own dialog, and nothing tells the
    /// panel they arrived. Only the one group is reloaded, and into the object that is already on screen,
    /// so its sort, its view and whether it is collapsed all survive -- which a full refresh would not
    /// leave alone.
    ///
    /// A source that has since gone (settings edited while the panel was up) simply leaves the group as
    /// it was: there is nothing to load it from, and emptying it would be a worse answer than stale.
    /// </remarks>
    public async Task ReloadGroupAsync(QuickPanelGroupViewModel group, CancellationToken token = default)
    {
        var source = SourceOf(group.SourceId);
        if (source == null) return;

        List<SearchResult> results;
        try
        {
            results = await _load(source, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"[QuickPanel] Source '{source.Path}' failed to reload: {ex.Message}", LogLevel.Error);
            return;
        }

        group.Replace(QuickPanelGroupLoader.Map(results));

        // A group that the reload emptied is hidden by its own HasMatches, so the panel's own "nothing
        // here" line has to be recomputed against what is left.
        IsEmpty = !Groups.Any(shown => shown.HasMatches);
        UpdateLineNumberSizing();
    }

}
