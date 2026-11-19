using System.Diagnostics;
using System.Windows.Threading;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.ViewModels.Search.DynamicSidebar;

// Mutate and count on one ordered background chain. Only the latest count is published to the UI.
internal sealed class SearchSidebarCountHelper : IDisposable
{
    private readonly IReadOnlyList<DynamicSidebarGroupViewModel> _groups;
    private readonly IReadOnlyList<DynamicSidebarItemViewModel> _items;
    private readonly IReadOnlyList<IReadOnlyList<Func<ISearchResult, bool>>> _predicates;
    private readonly Dispatcher _dispatcher;
    private SidebarFilterCountAccumulator _accumulator;
    private CancellationTokenSource _query = new();
    private Task _work = Task.CompletedTask;
    private int _version;
    private DateTime _lastPublish = DateTime.MinValue;
    private bool _hasResults;
    internal Task PendingUpdate => _work;

    public SearchSidebarCountHelper(IReadOnlyList<DynamicSidebarGroupViewModel> groups)
    {
        _groups = groups;
        _items = groups.SelectMany(group => group.Items).ToList();
        _predicates = groups.Select(group =>
            (IReadOnlyList<Func<ISearchResult, bool>>)group.Items.Select(item => item.MatchPredicate).ToArray()).ToArray();
        _accumulator = new SidebarFilterCountAccumulator(_predicates);
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public void Reset()
    {
        _query.Cancel();
        _query.Dispose();
        _query = new CancellationTokenSource();
        _accumulator = new SidebarFilterCountAccumulator(_predicates);
        _work = Task.CompletedTask;
        Interlocked.Increment(ref _version);
        _lastPublish = DateTime.MinValue;
        _hasResults = false;
        foreach (var item in _items) item.ClearCount();
    }

    public void Update(IReadOnlyList<AppSearchResult> batch, bool final)
    {
        // The producer reuses its LastBatchRows buffer after the dispatcher returns.
        var snapshot = batch.ToArray();
        _hasResults = true;
        var publish = final || DateTime.UtcNow - _lastPublish >= TimeSpan.FromMilliseconds(120);
        Queue(accumulator => accumulator.AddBatch(snapshot), publish);
    }

    public void Recalculate()
    {
        if (_hasResults) Queue(_ => { }, true);
    }

    public void Replace(IReadOnlyList<AppSearchResult> results)
    {
        // Search dispatch owns a stable published list, so this can cross the await without a UI copy.
        _hasResults = true;
        Queue(accumulator => accumulator.ReplaceResults(results), true);
    }

    private void Queue(Action<SidebarFilterCountAccumulator> update, bool publish)
    {
        if (_items.Count == 0) return;
        var selected = _groups.Select(group =>
            (IReadOnlyList<Func<ISearchResult, bool>>)group.Items.Where(item => item.IsSelected)
                .Select(item => item.MatchPredicate).ToArray()).ToArray();
        var version = publish ? Interlocked.Increment(ref _version) : Volatile.Read(ref _version);
        var token = _query.Token;
        var accumulator = _accumulator;
        var previous = _work;
        if (publish) _lastPublish = DateTime.UtcNow;
        _work = Task.Run(async () =>
        {
            try
            {
                await previous.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                update(accumulator);
                if (!publish || version != Volatile.Read(ref _version)) return;
                var clock = Stopwatch.StartNew();
                var counts = accumulator.Calculate(selected, token).ToArray();
                await _dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested || version != Volatile.Read(ref _version)) return;
                    for (var i = 0; i < _items.Count; i++) _items[i].SetCount(counts[i]);
                }, DispatcherPriority.Background, token).Task.ConfigureAwait(false);
                SearchPerf.Log($"[sidebar.publish] predicates={accumulator.PredicateCount} elapsed={clock.ElapsedMilliseconds}ms");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { Logger.Log($"[SearchSidebarCounts] Counting failed: {ex}", LogLevel.Error); }
        }, token);
    }

    public void Dispose()
    {
        _query.Cancel();
        _query.Dispose();
        Interlocked.Increment(ref _version);
    }
}
