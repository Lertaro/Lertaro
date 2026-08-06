namespace Lertaro.App;

// Shared shape behind AppSearchResult's Icon/DateModified lazy loads: run work on a background
// thread gated by a caller-owned semaphore (throttles concurrent background work across all result
// instances), then marshal back to the UI thread to apply it.
internal static class LazyBackgroundLoader
{
    public static void Start(SemaphoreSlim semaphore, Func<Task> loadAndApply) => Task.Run(async () =>
                                                                                       {
                                                                                           await semaphore.WaitAsync();
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
