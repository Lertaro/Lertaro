namespace Lertaro.App.Services.QuickPanel;

// When losing the foreground means the panel goes, and when it does not. Split out of
// QuickPanelManager.cs purely to keep that file under the repo's per-file line limit.
public sealed partial class QuickPanelManager
{
    /// <summary>Closes the panel a moment from now, unless by then it should not be closed.</summary>
    /// <remarks>
    /// The same 200ms the quick window's deactivate handler uses, and for the same two reasons. A
    /// foreground steal that bounces straight back (a background thread waking a WSL VM flashes a
    /// conhost, to take the case that window documents) is not the user leaving. And what the foreground
    /// became is only knowable afterwards -- which is what the preview check needs, since clicking into
    /// the preview deactivates this window and must not close it.
    ///
    /// Every gate is re-asked rather than trusted from the moment the event fired: 200ms is long enough
    /// for the pin to be pressed or the flyout to open, and a stale answer would close the panel out from
    /// under either.
    /// </remarks>
    private void ScheduleDismiss()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            // Gone already, or the foreground came straight back.
            if (_window is not { } window || window.IsActive) return;

            // Not while the window is being dragged: DragMove runs a modal loop the window comes out of
            // deactivated, which is not the user clicking away.
            if (window.IsDraggingWindow) return;

            // Nor when this summon was asked to stay, by the pin or the same hotkey the quick window uses
            // for it. This is also how a drag from Explorer into the panel is possible at all: that drag
            // begins by clicking Explorer, which is this very event, so the panel has to have been pinned
            // first. It used to stay up on its own whenever the workspace had a droppable group, which
            // was a rule the user could neither see nor turn off, on a panel whose whole habit is to get
            // out of the way. The pin says the same thing, deliberately and visibly.
            if (window.IsStayOpen) return;

            // Nor while the action flyout is up. It hangs its key handler on the panel window and needs
            // it alive to reach it, so closing here would take the menu down with the panel and leave
            // every shortcut on it looking dead.
            if (ShellMenu.ActionFlyout.ActionFlyout.IsOpen) return;

            // The preview this panel opened is a window the panel put on screen and the user reached from
            // a row in it -- scrolling a document or playing a video in there needs real focus. Clicking
            // it is not clicking away.
            if (QuickLookManager.Instance.IsPreviewForeground()) return;

            // A preview rendered by something out of process (a native handler's own window, or an
            // external viewer docked by a plugin) holds the OS foreground legitimately. The quick window
            // consults the same signal on this same path.
            if (PluginSdk.Services.PreviewActivationSignal.IsActive) return;

            Hide();
        };
        timer.Start();
    }
}
