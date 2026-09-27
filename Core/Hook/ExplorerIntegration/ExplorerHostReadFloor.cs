namespace Lertaro.Core.Hook;

/// <summary>
/// Decides whether the poller may ask a host application for its current path right now: somebody has to
/// be waiting for it, and the same window cannot be asked twice in quick succession.
/// </summary>
/// <remarks>
/// Both halves exist because of one measured symptom: XYplorer's file info tip could not stay visible while
/// the XYplorer plugin was enabled. Its tip is a plain tooltips_class32 of XYplorer's own process, and every
/// attempt lived 171-235ms -- the poller's own settle period. The read is the cause, not the event: for a
/// host whose path is read through a script API (XYplorer answers <c>&lt;curpath&gt;</c> over a WM_COPYDATA
/// round trip ON ITS OWN UI THREAD), the read cancels the transient UI the read was asked about. Proof by
/// subtraction: with no adapter or collector claiming the window -- so nothing reading it at all -- the tip
/// behaves normally.
///
/// Frequency alone did not fix it: spacing reads to one per interval merely changed which tips died (the
/// reported symptom became alternating, one short one long), because pointer movement inside the host keeps
/// asking. So asking is now gated on demand. Pointer movement is never demand: a file manager's panes and
/// status bar raise name-change and focus events continuously while the mouse moves, and those events are
/// indistinguishable from the ones a real folder change raises -- that is how a folder change reports.
///
/// Demand has three sources: a real foreground change (the user moved between windows, and the inline card
/// needs this window's folder the moment it opens), a keystroke that may summon the card, and the card being
/// on screen (its scope and dock still have to follow the host while it is up).
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
