using System.Runtime.InteropServices;

namespace Lertaro.App.Services;

// Windows' Process Power Throttling (EcoQoS) can deprioritize this process at the OS scheduling level
// whenever it isn't the foreground window -- which, for a hotkey-summoned launcher, is nearly all the
// time. Thread.CurrentThread.Priority in App.xaml.cs only affects scheduling WITHIN this process; it
// does nothing against throttling applied to the whole process from outside. On battery, that combination
// (mostly-hidden process + EcoQoS) is the likely cause of a sluggish first frame when a search window is
// summoned. Opting out only while a window is actually visible keeps the battery-saving behavior for the
// other 99% of the time this app spends hidden in the background.
public static class PowerThrottlingHelper
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr hProcess, PROCESS_INFORMATION_CLASS ProcessInformationClass, ref PROCESS_POWER_THROTTLING_STATE ProcessInformation, uint ProcessInformationSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private enum PROCESS_INFORMATION_CLASS
    {
        ProcessPowerThrottling = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    private static readonly object _lock = new();
    private static readonly HashSet<string> _visibleWindows = new(StringComparer.Ordinal);

    // windowId is just a stable per-surface key ("quick", "inline", ...) -- a HashSet rather than a raw
    // counter so a stray extra Hide call (or a Show call that fires twice for the same surface, e.g. a
    // redundant EnsureWindowCreated()) is a harmless no-op instead of drifting the count out of balance,
    // which would otherwise leave throttling stuck either on or off until the next process restart.
    public static void WindowShowing(string windowId)
    {
        lock (_lock)
        {
            if (_visibleWindows.Add(windowId) && _visibleWindows.Count == 1)
                SetThrottling(throttlingAllowed: false);
        }
    }

    public static void WindowHidden(string windowId)
    {
        lock (_lock)
        {
            if (_visibleWindows.Remove(windowId) && _visibleWindows.Count == 0)
                SetThrottling(throttlingAllowed: true);
        }
    }

    private static void SetThrottling(bool throttlingAllowed)
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = throttlingAllowed ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0
            };
            SetProcessInformation(GetCurrentProcess(), PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        catch
        {
            // Best-effort -- SetProcessInformation/ProcessPowerThrottling needs Windows 10 1709+;
            // failing here just means throttling stays at whatever Windows would otherwise apply.
        }
    }
}
