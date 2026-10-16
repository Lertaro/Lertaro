using System.Text;
using Lertaro.PluginSdk.Registries;

namespace Lertaro.Core.Hook;

/// <summary>
/// Finds the folder Quick Switch should land on in a session that has not seen a file manager yet.
/// </summary>
/// <remarks>
/// The tracker only learns a folder while the window showing it is the ACTIVE one, and the Hook process is
/// launched for the App that was just started -- which is the foreground window at that moment. So the first
/// Ctrl+G of a session routinely found no folder at all and did nothing, until the user happened to click a
/// file manager. The windows that are already open are the answer: the topmost one a registered provider
/// recognizes is the file manager the user was last in.
///
/// ponytail: Z order, not a history. It is wrong when the user's last file-manager window is buried under
/// browser windows, which costs one skipped Ctrl+G rather than a navigation to the wrong folder.
/// </remarks>
internal sealed class ExplorerPathSeeder
{
    // Enough to get past the windows that are never providers (the shell, browser and editor windows a user
    // has open) without probing every window on the desktop: each probe can cross a process boundary.
    private const int MaxCandidates = 12;
    private const int ReadBudgetMs = 300;

    private readonly ExplorerTracker _tracker;

    internal ExplorerPathSeeder(ExplorerTracker tracker) => _tracker = tracker;

    /// <summary>Reads and publishes the topmost provider window's folder, once, if nothing is known yet.</summary>
    internal void SeedWhenNothingIsKnown()
    {
        if (!string.IsNullOrEmpty(_tracker.LastActiveExplorerPath)) return;

        try
        {
            var probed = 0;
            foreach (var hwnd in ExplorerNativeHooks.TopLevelWindowsInZOrder())
            {
                if (probed >= MaxCandidates) return;
                if (!IsWorthProbing(hwnd)) continue;
                probed++;

                if (TryRead(hwnd, out _)) return;
            }
        }
        catch (Exception ex)
        {
            // Best effort by definition: this runs while the tracker is starting, and a session without a
            // seeded folder is the behaviour it had before this existed.
            Logger.Log($"[ExplorerTracker] Seeding the last browsed folder failed: {ex.Message}", LogLevel.Warn);
        }
    }

    private bool IsWorthProbing(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || hwnd == ExplorerNativeHooks.GetShellWindow()) return false;
        if (!ExplorerNativeHooks.IsWindowVisible(hwnd) || ExplorerNativeHooks.IsIconic(hwnd)) return false;
        // Owned windows are dialogs and popups of something else, never a file manager browser window.
        if (ExplorerNativeHooks.GetAncestor(hwnd, ExplorerNativeHooks.GA_ROOTOWNER) != hwnd) return false;

        ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var processId);
        return processId != 0 && processId != (uint)Environment.ProcessId;
    }

    private bool TryRead(IntPtr hwnd, out string? path)
    {
        path = null;
        var className = ClassNameOf(hwnd);
        var processName = ProcessNameResolver.GetNameWithoutExtension(ProcessIdOf(hwnd));
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(processName)) return false;

        // Bounded: a provider's probe can reach into the target process (see ExplorerStaInvoker), and this runs
        // on the tracker thread before the hooks are installed.
        var budget = TimeSpan.FromMilliseconds(ReadBudgetMs);
        var adapter = ExplorerStaInvoker.RunOnStaWithTimeout(
            () => InlineSearchAdapterRegistry.GetMatchingAdapter(hwnd, className, processName), null, budget);
        var collector = adapter == null || !adapter.IsFileExplorer
            ? ExplorerStaInvoker.RunOnStaWithTimeout(
                () => ActivePathCollectorRegistry.GetCollectors().FirstOrDefault(c => c.CanHandle(hwnd, className, processName)), null, budget)
            : null;

        if (adapter is { IsFileExplorer: true })
            path = ExplorerStaInvoker.RunOnStaWithTimeout(() => adapter.GetSearchScope(hwnd), null, budget);
        else if (collector != null)
            path = ExplorerStaInvoker.RunOnStaWithTimeout(
                () => collector.TryGetPath(hwnd, className, hwnd, className, processName), null, budget);

        if (string.IsNullOrEmpty(path)) return false;

        Logger.Log($"[ExplorerTracker] Seeded the last browsed folder from 0x{hwnd:x} ({className}): {path}", LogLevel.Info);
        // Under the lock, because this runs on a pool thread while the WinEvent tracker thread is classifying
        // windows into the same state: both the path and the provider list are that state's fields.
        lock (_tracker.StateLock)
        {
            // ...and not at all once the tracker has stopped: a seed is a snapshot taken seconds ago, and a
            // stopped tracker's state is not something to publish into a running App's mirror.
            if (!MayPublish(_tracker.IsRunning, hwnd, path)) return false;

            _tracker.UpdatePath(path, false);
            // Remembered after publishing, and only now that a folder is actually known: an entry with no path
            // cannot be chosen for a switch, and it would push a real one out of the list.
            _tracker.RememberPathProvider(adapter, hwnd, path);
        }
        return true;
    }

    /// <summary>Whether a read folder may still be published: only a running tracker has state to publish into.</summary>
    internal static bool MayPublish(bool isRunning, IntPtr hwnd, string? path) =>
        isRunning && hwnd != IntPtr.Zero && !string.IsNullOrEmpty(path);

    private static string ClassNameOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        ExplorerNativeHooks.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static uint ProcessIdOf(IntPtr hwnd)
    {
        ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var processId);
        return processId;
    }
}
