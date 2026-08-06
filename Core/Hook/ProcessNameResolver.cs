using System.Text;
using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Hook;

/// <summary>
/// Resolves a running process's executable name from its id.
/// </summary>
/// <remarks>
/// Deliberately does not reach for <see cref="System.Diagnostics.Process.GetProcessById(int)"/> first. That
/// call enumerates every process on the machine just to validate the id, then hands back a finalizable
/// object -- far more than a name lookup needs on paths that run per window event and per keystroke. The
/// Win32 route here is an open, one query and a close, allocating nothing but the string itself.
/// </remarks>
internal static class ProcessNameResolver
{
    // Unlike PROCESS_QUERY_INFORMATION this is granted for processes running at a higher integrity level
    // than ours, which matters because the hook inspects whatever the user happens to have in front.
    private const uint ProcessQueryLimitedInformation = 0x1000;

    // Consecutive keystrokes and consecutive window events overwhelmingly concern the same process, so one
    // slot collapses a burst into a single lookup. Held only briefly because process ids are reused: after
    // this long, a slot whose process has exited and whose id has been handed to something else is gone.
    private const int CacheLifetimeMs = 1000;

    private sealed record CachedPath(uint ProcessId, string ImagePath, long Ticks);

    private static CachedPath? _cache;

    /// <summary>
    /// Full path of the process's executable. False when it cannot be read -- the process has already
    /// exited, or it is a protected/system process we are not allowed to open.
    /// </summary>
    public static bool TryGetImagePath(uint processId, out string imagePath) =>
        TryGetImagePathCore(processId, QueryImagePath, out imagePath);

    // The lookup is passed in so the caching can be exercised without depending on real processes.
    internal static bool TryGetImagePathCore(uint processId, Func<uint, string> queryImagePath, out string imagePath)
    {
        imagePath = string.Empty;
        if (processId == 0)
            return false;

        // Read the slot once. It is only ever replaced wholesale, so a reader on another thread sees either
        // the old entry or the new one intact -- never a process id paired with someone else's path.
        var cached = _cache;
        if (cached is not null && cached.ProcessId == processId && Environment.TickCount64 - cached.Ticks < CacheLifetimeMs)
        {
            imagePath = cached.ImagePath;
            return true;
        }

        var path = queryImagePath(processId);
        if (path.Length == 0)
            return false;

        _cache = new CachedPath(processId, path, Environment.TickCount64);
        imagePath = path;
        return true;
    }

    private static string QueryImagePath(uint processId)
    {
        var handle = KeyboardNativeMethods.OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
            return string.Empty;

        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return KeyboardNativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size)
                ? buffer.ToString()
                : string.Empty;
        }
        finally
        {
            KeyboardNativeMethods.CloseHandle(handle);
        }
    }

    /// <summary>
    /// The executable name with its extension dropped ("explorer"), which is the form
    /// <see cref="System.Diagnostics.Process.ProcessName"/> returns. Plugin adapters compare against it
    /// exactly (<c>processName.Equals("dopus")</c>), so an extension must never leak through.
    /// </summary>
    public static string GetNameWithoutExtension(uint processId, string fallback = "Unknown")
    {
        if (TryGetImagePath(processId, out var imagePath))
            return Path.GetFileNameWithoutExtension(imagePath);

        // Opening a process can fail where the managed route still answers, since that one reads the
        // system-wide process list rather than the process itself. Kept as a fallback -- just no longer
        // the default, and disposed this time.
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return fallback;
        }
    }
}
