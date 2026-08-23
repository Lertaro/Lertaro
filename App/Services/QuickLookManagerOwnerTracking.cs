using System.Windows;
using Lertaro.PluginSdk.Abstractions.Plugins.Preview;
using Lertaro.App.Services.Plugin;

namespace Lertaro.App.Services;

// Split out of QuickLookManager.cs to keep that file under the repo's per-file line limit.
// Handles owner window tracking, focus deactivation, and session cleanup.
public partial class QuickLookManager
{
    private void AttachOwnerLocationTracking(Window owner)
    {
        if (_ownerTrackingAttached) return;
        owner.LocationChanged += Owner_LocationChanged;
        owner.SizeChanged += Owner_SizeChanged;
        _ownerTrackingAttached = true;
    }

    private void AttachOwnerDeactivateTracking(Window owner)
    {
        if (_ownerDeactivateAttached) return;
        owner.Deactivated += Owner_Deactivated;
        _ownerDeactivateAttached = true;
    }

    private void DetachOwnerDeactivateTracking()
    {
        if (!_ownerDeactivateAttached || _owner == null) return;
        _owner.Deactivated -= Owner_Deactivated;
        _ownerDeactivateAttached = false;
    }

    private void DetachOwner()
    {
        if (_owner != null)
        {
            if (_ownerTrackingAttached)
            {
                _owner.LocationChanged -= Owner_LocationChanged;
                _owner.SizeChanged -= Owner_SizeChanged;
                _ownerTrackingAttached = false;
            }
            DetachOwnerDeactivateTracking();
            _owner = null;
        }
    }

    // Branches on the current mode: external-dock re-asserts QuickLook's window position, the normal
    // path repositions our own _window -- both are hooked to the same owner LocationChanged/SizeChanged
    // events (see AttachOwnerLocationTracking), just handled differently depending on which is active.
    private void RepositionForCurrentMode()
    {
        if (_window == null || _owner == null) return;
        if (_window.IsShowingExternalPreview) NotifyExternalBounds(_owner);
        else PositionWindow();
    }

    private void Owner_LocationChanged(object? sender, EventArgs e) => RepositionForCurrentMode();
    private void Owner_SizeChanged(object? sender, SizeChangedEventArgs e) => RepositionForCurrentMode();

    private void Owner_Deactivated(object? sender, EventArgs e)
    {
        // A real (HwndHost) preview -- e.g. a native document/media preview handler -- needs actual focus
        // to be interactive (scrolling, playback controls), so clicking into it deactivates the owner for
        // real. Without this check, that click would immediately hide the very preview the user just
        // clicked into. Only hide when something outside this process took the foreground.
        if (IsForegroundWindowInThisProcess())
            return;

        // Dragging the preview's header out to another application makes that application the foreground
        // window, which lands here. Hiding now would pull this window out from under the DoDragDrop still
        // running on its own header -- the same hazard the inline window's own teardown guards against
        // with this flag. The drag's own completion hides the search windows anyway (see
        // ResultsDragDropHelper.HideSearchWindows).
        if (Views.Controls.Results.ResultsDragDropHelper.IsDragActive)
            return;

        Hide();
    }

    /// <summary>Raised when the preview itself loses the foreground to another application.</summary>
    public event Action? PreviewFocusLost;

    private void OnPreviewDeactivated(object? sender, EventArgs e)
    {
        // Clicking back into the owner, or onto any other window of this app's, is not leaving.
        if (IsForegroundWindowInThisProcess()) return;

        // A drag out of the preview's own header makes the drop target's application the foreground, the
        // same hazard Owner_Deactivated guards against for the same reason.
        if (Views.Controls.Results.ResultsDragDropHelper.IsDragActive) return;

        PreviewFocusLost?.Invoke();
    }

    private static bool IsForegroundWindowInThisProcess()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(fg, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    private void OnSessionOwnerClosed(object? sender, EventArgs e)
    {
        if (sender is Window w)
        {
            w.Closed -= OnSessionOwnerClosed;
            _sessionOwners.Remove(w);
        }
        // The owner is already deactivated → QuickLook hidden → any visible host parked its handler back
        // in the pool, so releasing now can't blank a live preview.
        foreach (var provider in PluginManager.Instance.FilePreviewProviders)
            (provider as IPreviewSessionAware)?.EndPreviewSession();
    }
}
