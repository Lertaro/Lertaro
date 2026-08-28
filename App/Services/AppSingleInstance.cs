namespace Lertaro.App.Services;

/// <summary>
/// Owns the per-session named mutex that keeps the App single-instance.
/// Split out of App.xaml.cs purely to keep that file under the repo's per-file line limit.
/// </summary>
public static class AppSingleInstance
{
    /// <summary>
    /// Acquires the single-instance mutex, surviving an instance that crashed while holding it:
    /// requesting initial ownership of an abandoned named mutex throws AbandonedMutexException, and
    /// the dead owner means no live instance exists -- so ownership falls to this process, and it
    /// runs as the single instance instead of aborting startup.
    /// </summary>
    public static Mutex AcquireMutex(string mutexName, out bool createdNew)
    {
        try
        {
            return new Mutex(true, mutexName, out createdNew);
        }
        catch (AbandonedMutexException)
        {
            // Re-open the same named mutex without requesting initial ownership: the abandoned
            // acquisition already passed ownership to this process, and the handle keeps the mutex
            // itself alive for this process's lifetime.
            createdNew = true;
            return new Mutex(false, mutexName);
        }
    }
}
