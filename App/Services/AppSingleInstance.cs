namespace Lertaro.App.Services;

/// <summary>
/// Owns the per-session named mutex that keeps the App single-instance.
/// Split out of App.xaml.cs purely to keep that file under the repo's per-file line limit.
/// </summary>
public static class AppSingleInstance
{
    /// <summary>
    /// Acquires the single-instance mutex, including taking over one abandoned by a crashed instance.
    /// </summary>
    public static Mutex AcquireMutex(string mutexName, out bool createdNew)
    {
        var mutex = new Mutex(true, mutexName, out createdNew);
        if (createdNew)
            return mutex;

        try
        {
            // The constructor only requests initial ownership when it creates the mutex. A zero-time
            // wait is therefore needed for an existing mutex: it returns false for a live instance,
            // and throws after taking ownership when the previous owner exited without releasing it.
            if (mutex.WaitOne(0))
                createdNew = true;
        }
        catch (AbandonedMutexException)
        {
            // WaitOne reports abandonment after transferring ownership to this thread. Keep and return
            // this same handle so the caller can release the ownership during process shutdown.
            createdNew = true;
        }

        return mutex;
    }
}
