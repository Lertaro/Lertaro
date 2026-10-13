using Lertaro.Core.Services;
using System.Runtime.CompilerServices;

namespace Lertaro.Core.Tests.Services;

[TestClass]
public class AbandonedTaskTests
{
    // Only events carrying this exception are counted: TaskScheduler.UnobservedTaskException is
    // process-global, and other tests running in parallel fault tasks of their own.
    private sealed class ScanFaultException : Exception;

    [TestMethod]
    public async Task KeepObserved_FaultAfterTheWaitIsAbandoned_IsNotReportedUnobserved()
    {
        // The control has to raise the event, or a passing result would only mean the fault was never
        // finalized at all.
        Assert.AreEqual(1, await CountUnobservedFaults(keepObserved: false),
            "a scan abandoned by WaitAsync and faulting afterwards is reported by the finalizer");
        Assert.AreEqual(0, await CountUnobservedFaults(keepObserved: true),
            "KeepObserved stands between that fault and the crash dialog");
    }

    // SearchService's shape: a directory walk still running when the query that started it is superseded,
    // so the WaitAsync covering it is abandoned before the walk finishes.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<int> CountUnobservedFaults(bool keepObserved)
    {
        var count = 0;
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            if (args.Exception.Flatten().InnerExceptions.OfType<ScanFaultException>().Any())
            {
                count++;
                args.SetObserved();
            }
        }

        var stillWalking = new ManualResetEventSlim(false);
        var search = new CancellationTokenSource();
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            var pending = StartAbandonedScan(stillWalking, search, keepObserved);
            try
            {
                await pending.WaitAsync(search.Token);
                Assert.Fail("the superseded query should have abandoned this wait");
            }
            catch (OperationCanceledException)
            {
            }

            stillWalking.Set();
            // Nothing may reference the scan now; the report only arrives once the GC finalizes it.
            pending = null!;
            for (var round = 0; round < 10 && count == 0; round++)
            {
                await Task.Delay(20).ConfigureAwait(false);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            return count;
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
            search.Dispose();
            stillWalking.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<List<int>> StartAbandonedScan(
        ManualResetEventSlim stillWalking, CancellationTokenSource search, bool keepObserved)
    {
        search.Cancel();
        var pending = Task.Run(() => FinishScan(stillWalking));
        return keepObserved ? pending.KeepObserved() : pending;
    }

    private static List<int> FinishScan(ManualResetEventSlim stillWalking)
    {
        stillWalking.Wait();
        throw new ScanFaultException();
    }
}
