namespace Lertaro.App;

// Shared shape behind AppSearchResult's Icon/DateModified lazy loads: run work on a background
// thread gated by a caller-owned semaphore (throttles concurrent background work across all result
// instances), then marshal back to the UI thread to apply it.
internal static class LazyBackgroundLoader
{
    // onWaitTimedOut runs when the permit has not come free within waitTimeoutMs, which is how a caller gets
    // its row back into a retryable state instead of leaving it on whatever it is already showing. The task is
    // returned so a caller that cares -- and every test -- can await the load instead of polling for it.
    public static Task Start(SemaphoreSlim semaphore, Func<Task> loadAndApply,
                             Action? onWaitTimedOut = null, int waitTimeoutMs = 5000) => Task.Run(async () =>
                             {
                                 if (!await semaphore.WaitAsync(waitTimeoutMs))
                                 {
                                     onWaitTimedOut?.Invoke();
                                     return;
                                 }

                                 try
                                 {
                                     await loadAndApply();
                                 }
                                 catch
                                 {
                                     // Ignore -- loadAndApply is responsible for its own fallback state on failure
                                 }
                                 finally
                                 {
                                     semaphore.Release();
                                 }
                             });

    public static void ApplyOnUiThread(Action apply)
    {
        var app = System.Windows.Application.Current;
        if (app != null)
        {
            _ = app.Dispatcher.BeginInvoke(apply, System.Windows.Threading.DispatcherPriority.Background);
        }
        else
        {
            apply();
        }
    }
}
