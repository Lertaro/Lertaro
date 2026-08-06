namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Set by a preview provider for as long as an out-of-process native handler (e.g. an Office document's
/// Preview Handler COM server, which is the full Office application acting as its own prevhost
/// surrogate) is actively hosted -- not just its initial cold-start, but the whole viewing session, since
/// interacting with its rendered content (e.g. a right-click context menu) can pop up a real top-level
/// window of that same process at any point. The quick window's foreground-loss hide checks this so that
/// isn't mistaken for the user switching to another app. A depth counter rather than a bool, so more than
/// one hosted handler's Begin/End pairs (e.g. two preview hosts alive at once) don't let one End() turn it
/// off while another is still active.
/// </summary>
public static class PreviewActivationSignal
{
    private static int _depth;

    public static bool IsActive => Volatile.Read(ref _depth) > 0;

    public static void Begin() => Interlocked.Increment(ref _depth);
    public static void End() => Interlocked.Decrement(ref _depth);

    /// <summary>
    /// Raised by a preview provider the moment it detects that the out-of-process handler it just
    /// granted foreground rights to has actually taken OS keyboard focus for itself -- e.g. Excel's own
    /// window grabbing focus once its content finishes initializing, anywhere from tens of milliseconds
    /// to well over a second later depending on the file. The app subscribes once to reclaim focus back
    /// onto its search box exactly when this fires, instead of guessing at a fixed delay.
    /// </summary>
    public static event Action? FocusStolen;

    public static void NotifyFocusStolen() => FocusStolen?.Invoke();
}
