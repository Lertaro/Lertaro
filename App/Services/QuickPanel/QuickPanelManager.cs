using System.Runtime.InteropServices;
using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.App.Views.QuickPanel;

namespace Lertaro.App.Services.QuickPanel;

/// <summary>
/// Owns the quick panel: its lifetime and where it docks. The hotkey that opens it belongs to the hook
/// service, which sends QuickPanelHotkey when the configured combination fires.
/// </summary>
/// <remarks>
/// A window per open, closed on the way out, deliberately unlike the quick window which is reused. What
/// that window costs to build lands inside the load this already awaits, and what it buys is that no
/// state can survive an open it was not meant to -- see the note on its construction below for the five
/// defects that did.
///
/// The view model outlives them. It holds what should carry across (which workspace was active, what the
/// user did to a group while the panel was up) and nothing that is the window's own.
///
/// Show returns without opening when there is nothing to show. An empty shell over the window in front
/// would only be in the way.
/// </remarks>
public sealed partial class QuickPanelManager : IDisposable
{

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public static QuickPanelManager? Instance { get; private set; }

    private QuickPanelWindow? _window;
    private QuickPanelViewModel? _viewModel;

    public QuickPanelManager() => Instance = this;


    public void Toggle()
    {
        // Before the foreground is even read, let alone shown to. A shell light-dismiss overlay -- the
        // Start Menu, Search, the notification centre -- does not hand over keyboard focus the normal
        // way: once anything of ours calls Show or SetForegroundWindow, GetForegroundWindow() starts
        // reporting US while the overlay silently keeps every keystroke, and no API reveals the
        // mismatch afterwards. So it has to be caught while it is still truthfully foreground. The quick
        // window does the same thing at the same point, for the same reason.
        //
        // Earlier here than there, because this panel also DOCKS to the foreground window: summoned
        // over an open Start Menu it would otherwise pin itself to the Start Menu's corner and come up
        // unable to receive a keystroke.
        Views.QuickSearchWindow.Helpers.ShellOverlayDismissHelper.DismissOverlayIfForeground();

        // Read the foreground window before showing anything: the panel docks to whatever was in front
        // at the moment it was asked for, and showing first would make that "whatever was in front
        // before us", which is only the same window by luck.
        var host = GetForegroundWindow();

        if (_window is { IsVisible: true })
        {
            Hide();
            return;
        }

        // The panel is a companion for other applications. When a Lertaro window is already in front,
        // opening another Lertaro surface only obscures the window the user is working in. Keep the
        // visible-panel toggle above this check so the same hotkey can still close an open panel.
        if (IsCurrentProcessWindow(host)) return;

        var settings = Core.UserSettings.Load();
        if (!settings.QuickPanel.Enabled) return;

        // The same window the panel would dock to is the one that decides whether it may open at all and
        // which workspace it opens on, so its process is read once here and carried through both.
        var process = ProcessNameOf(host);
        if (Core.QuickPanelTabSelection.IsBlocked(process, settings))
        {
            Core.Logger.Log($"[QuickPanel] '{process}' is blacklisted, so not opening.", Core.LogLevel.Debug);
            return;
        }

        Show(host, process);
    }

    internal static bool IsCurrentProcess(uint foregroundProcessId, uint currentProcessId) =>
        foregroundProcessId != 0 && foregroundProcessId == currentProcessId;

    private static bool IsCurrentProcessWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        try
        {
            Core.Hook.ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var processId);
            return IsCurrentProcess(processId, (uint)Environment.ProcessId);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The foreground window's process name, which is what the workspace rules match on.</summary>
    private static string? ProcessNameOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;

        try
        {
            Core.Hook.ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            // The window can be gone by the time its process is asked for. Null means "no app in front
            // worth naming", which every rule below already treats as "no claim, no block".
            return null;
        }
    }

    private bool _showing;

    private async void Show(IntPtr host, string? process)
    {
        // Show awaits a real load, and the hotkey that got here can be pressed again while it does --
        // at which point Toggle still sees a window that is not visible yet and starts a second one.
        // Two loads racing into the same collections is not a state worth reasoning about.
        if (_showing) return;
        _showing = true;
        try
        {
            await ShowCoreAsync(host, process);
        }
        finally
        {
            _showing = false;
        }
    }

    private async Task ShowCoreAsync(IntPtr host, string? process)
    {
        // As early as possible, before Show() -- Windows' Power Throttling operates at the process
        // scheduling level, so lifting it only after this window has already started painting would miss
        // the very first frame the user is waiting on. See PowerThrottlingHelper's own comment.
        PowerThrottlingHelper.WindowShowing("quickpanel");
        // Cancels a trim that is armed but has not fired, before anything else runs: emptying the
        // working set moments before a summon is strictly worse than never emptying it.
        IdleWorkingSetTrimmer.WindowShowing();

        _viewModel ??= new QuickPanelViewModel();

        // Loaded before there is a window to put it in: whether one is built at all depends on what this
        // turns up, and the panel shows what is recent and where you currently are, so it has to be
        // asked on every open rather than once.
        await _viewModel.RefreshAsync(process);

        // Nothing to show, nothing to open: the panel exists to put content over the window in front,
        // and flashing an empty shell over it would only be in the way.
        if (!_viewModel.HasContent)
        {
            PowerThrottlingHelper.WindowHidden("quickpanel");
            Core.Logger.Log("[QuickPanel] Nothing to show, so not opening.", Core.LogLevel.Debug);
            return;
        }


        // A window per open, closed on the way out. The view model outlives them, so where the user left
        // the panel -- the active workspace, and what they did to a group while it was up -- carries
        // across; everything that is purely the window's is destroyed with it.
        //
        // This replaced a single reused window, and the reason is worth keeping: five separate defects
        // came out of that one instance holding state between opens. A drag snapshot stranded on its
        // adorner layer. A hotkey reference to a list that had been torn down. The dismissal path
        // emptying a panel that had already reopened behind it. A first frame painted from the previous
        // open's containers. Each was fixed on its own; none of them could have happened here. Building
        // one costs about 60ms, which lands inside the load this already awaits.
        _window = new QuickPanelWindow(_viewModel);

        // The way back out of a preview the user clicked into. That click suspended this panel's own
        // dismissal (see Deactivated below), and this panel's Deactivated will not fire again -- so the
        // click that finally leaves for another application arrives here instead.
        void OnPreviewFocusLost()
        {
            if (_window is { IsStayOpen: true }) return;
            Hide();
        }

        QuickLookManager.Instance.PreviewFocusLost += OnPreviewFocusLost;

        // Closing is the only way out, so this is the one place the reference is dropped -- whether the
        // close came from Hide, from Escape, or from anything else that ever gets to close a window.
        _window.Closed += (_, _) =>
        {
            _window = null;
            QuickLookManager.Instance.PreviewFocusLost -= OnPreviewFocusLost;

            // The folder watchers only ever run while the panel is up: they exist to keep what is on
            // screen true, and there is no screen now.
            _viewModel?.StopWatching();

            // Armed, not done. The settings and full search windows trim their working set on Closed,
            // but those are closed rarely; this one closes as often as the quick window hides, which is
            // the case IdleWorkingSetTrimGate was written for after measuring it: the trim frees no
            // committed memory at all, and every evicted page is faulted back on the next summon --
            // ~17MB of them, in the phase that is 70% of a summon. So it waits for the process to go
            // genuinely quiet, and a burst of summons pays nothing.
            //
            // The window itself is garbage the moment this runs; the next collection has it, with no
            // help from here. Nor is the icon cache touched: it is shared with the quick window, and
            // dropping it here would make both re-resolve every icon.
            IdleWorkingSetTrimmer.WindowHidden();
            PowerThrottlingHelper.WindowHidden("quickpanel");
        };

        // Losing the foreground dismisses the panel, the way the inline window goes when the user clicks
        // away. Wired per window because there is a new one each open, which is also what stops these
        // handlers stacking up the way they would have on a reused one. What that means in full is in
        // QuickPanelManagerDismissal.cs.
        _window.Deactivated += (_, _) => ScheduleDismiss();

        // Positioned before Show: placing it afterwards lets the window paint once at its old location
        // and jump, which reads as a flicker every time the panel opens. A brand-new window has no old
        // location, but it does have a default one, and that is just as wrong to paint first.
        PositionAgainst(host);
        _window.Show();

        // After Show, and only then: the window has to exist as a real HWND before it can be given the
        // foreground. ShowActivated="False" means it comes up without focus by design, so this is what
        // actually hands it over.
        _window.ActivateAndFocus();

        // Only now, and only for as long as the panel is up: from here a change to any folder on screen
        // reloads the group showing it, whoever made the change -- a drop landing, a download finishing,
        // a file deleted from Explorer behind the panel.
        _viewModel.StartWatching();

    }


    private bool _hiding;

    /// <summary>Closes the panel. There is nothing to tidy up afterwards -- the window IS the state.</summary>
    /// <remarks>
    /// Closing raises Deactivated, which is wired to this same method, so without the guard the first
    /// close re-enters. The Closed handler is what drops the reference, so nothing here has to.
    /// </remarks>
    public void Hide()
    {
        if (_window == null || _hiding) return;

        _hiding = true;
        try
        {
            _window.Close();
        }
        finally
        {
            _hiding = false;
        }
    }


    public void Dispose()
    {

        _window?.Close();
        _window = null;

        if (ReferenceEquals(Instance, this)) Instance = null;
    }
}
