namespace Lertaro.Core.Hook;

/// <summary>Allows current-path reads only on demand and at a bounded frequency per window.</summary>
/// <remarks>
/// XYplorer handles its WM_COPYDATA path query on its UI thread, cancelling transient tooltips.
/// Observed tooltip lifetimes of 171–235 ms matched the poller's settle period. Throttling alone still
/// interrupted tips, so pointer-driven name/focus events must not trigger reads.
///
/// Read on a foreground change, a keystroke that may summon the card, or while the card is visible
/// and needs to track its host's folder and docking position.
/// </remarks>
internal sealed class ExplorerHostReadFloor
{
    // How long the SAME window is left alone after one read, so the remaining demand sources cannot read it
    // in a loop. Set wider than any tooltip's display time.
    internal const int MinHostReadMs = 2000;

    private IntPtr _hwnd;
    private long _notedTicks;
    private int _requested;
    private int _foreground;

    /// <summary>Steady demand: the inline window is on screen and has to keep following the host.</summary>
    public bool CardOnScreen { get; set; }

    internal bool HasPendingRead => Volatile.Read(ref _requested) != 0;

    /// <summary>
    /// One-shot demand, for a user action that needs the path now (a keystroke that may summon the card).
    /// Allocation- and lock-free: it is called from the low-level keyboard hook thread, where stalling past
    /// LowLevelHooksTimeout costs the hook silently.
    /// </summary>
    public void RequestRead() => Volatile.Write(ref _requested, 1);

    /// <summary>
    /// One-shot demand from a window switch, which additionally answers before the interval has passed: the
    /// card needs the new window's folder the moment it opens, and a switch cannot have been manufactured by
    /// hovering. Sticky for the same reason as RequestRead -- the poll it schedules may run later, and a
    /// request that only lived as "what the last event was" would be downgraded by whatever arrived after.
    /// </summary>
    public void RequestForegroundRead() => Volatile.Write(ref _foreground, 1);

    /// <summary>
    /// Whether a read for this window may start now. A pending request is consumed only when this says yes, so
    /// a request held back by the interval is not lost -- it is the next thing that runs once it ends.
    /// </summary>
    public bool AllowsRead(IntPtr hwnd, long nowTicks)
    {
        if (Volatile.Read(ref _foreground) == 1)
        {
            Volatile.Write(ref _foreground, 0);
            return true;
        }

        var requested = Volatile.Read(ref _requested) == 1;
        if (!requested && !CardOnScreen) return false;
        if (Blocks(hwnd, nowTicks)) return false;

        if (requested) Volatile.Write(ref _requested, 0);
        return true;
    }

    /// <summary>Whether this window is still inside its minimum interval since the last read.</summary>
    public bool Blocks(IntPtr hwnd, long nowTicks) =>
        hwnd != IntPtr.Zero
        && hwnd == _hwnd
        && nowTicks - _notedTicks < MinHostReadMs;

    /// <summary>Records that a read for this window is starting, beginning the interval.</summary>
    public void NoteRead(IntPtr hwnd, long nowTicks)
    {
        _hwnd = hwnd;
        _notedTicks = nowTicks;
    }

    /// <summary>Drops steady demand and any pending one-shot (the inline window went away, or the App link
    /// dropped): nothing is left to want the host's path.</summary>
    public void ClearCardOnScreen()
    {
        CardOnScreen = false;
        Volatile.Write(ref _requested, 0);
        Volatile.Write(ref _foreground, 0);
    }
}
