using Lertaro.PluginSdk;

namespace Lertaro.Plugins.TotalCommander.Win32;

// Runs blocking WM_COPYDATA queries on a throwaway STA thread with a bounded wait so a hung target
// cannot stall the caller indefinitely. Kept as its own file to stay under the repo's per-file line limit.
internal static class StaQueryHelper
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public static T Run<T>(Func<T> func, T fallback)
    {
        using var done = new ManualResetEventSlim(false);
        Exception? error = null;
        var result = fallback;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        })
        {
            IsBackground = true,
            Name = "TcWin32QuerySta"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!done.Wait(Timeout))
            Logger.Log("[TotalCommander] Win32 query timed out; returning null.", LogLevel.Warn);
        else if (error != null)
            Logger.Log($"[TotalCommander] Win32 query failed: {error.Message}", LogLevel.Warn);

        return result;
    }
}
