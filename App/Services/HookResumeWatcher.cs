using Microsoft.Win32;

namespace Lertaro.App.Services;

/// <summary>
/// Tells the hook process to put its low-level keyboard and mouse hooks back up whenever the machine
/// changes power state. Windows removes a low-level hook as soon as one of its callbacks overruns the
/// hook timeout -- and the handle it returned keeps looking installed afterwards, so nothing inside that
/// process can tell a dropped hook from a live one. Losing the hooks is what the "unresponsive after
/// sleep" report is: the summon key, the typed inline search and Quick Switch all live in that callback,
/// while the launcher's tray icon (this process) carries on as if nothing were wrong.
/// </summary>
/// <remarks>
/// Watched here, not in the hook: <see cref="SystemEvents"/> needs the desktop framework only this
/// project references, and the App is the half that is still alive when the hooks are gone. Like the
/// theme watcher and the notification screen events, the subscription belongs on a thread with a pump.
/// </remarks>
public static class HookResumeWatcher
{
    private static bool _subscribed;

    public static void EnsureWatching()
    {
        if (_subscribed) return;
        _subscribed = true;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    /// <summary>SystemEvents outlives the process that registered with it; leave nothing attached past shutdown.</summary>
    public static void StopWatching()
    {
        if (!_subscribed) return;
        _subscribed = false;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    /// <summary>
    /// Anything that is not the system going to sleep means "make sure the hooks are up".
    /// </summary>
    /// <remarks>
    /// Deliberately not a list of resume members: <see cref="PowerModes"/> carries only StatusChange,
    /// Suspend and Resume in .NET, so the early <c>PBT_APMRESUMEAUTOMATIC</c> -- the only resume a
    /// modern-standby machine may send, and the one that arrives before the user touches anything --
    /// never reaches a handler as a value of its own. A redundant re-arm is two Unhook/SetWindowsHookEx
    /// pairs, so absorbing the extra events costs nothing and keeps that one covered.
    /// </remarks>
    internal static bool MeansRearm(PowerModes mode) => mode != PowerModes.Suspend;

    private static void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (!MeansRearm(e.Mode)) return;

        // No dispatcher hop: the send is async and gated on the pipe's own write lock, and an idle hook
        // process has to be told as early as the event allows rather than queued behind UI work.
        Core.Logger.Log($"[HookResumeWatcher] Power event {(int)e.Mode} ({e.Mode}): re-arming the hook's low-level hooks.",
            Core.LogLevel.Info);
        App.HookClient?.RearmHooks();
    }
}
