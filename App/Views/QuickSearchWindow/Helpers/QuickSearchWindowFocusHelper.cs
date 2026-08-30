using System.Windows.Threading;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

// Split out to keep QuickSearchWindowController below the repository's per-file line limit; this
// helper owns the delayed keyboard-focus handoff used by the one window it receives.
internal sealed class QuickSearchWindowFocusHelper
{
    private readonly Lertaro.App.QuickSearchWindow _window;

    internal QuickSearchWindowFocusHelper(Lertaro.App.QuickSearchWindow window) => _window = window;

    // ForceForeground may complete through the elevated hook process asynchronously. Poll the real
    // foreground window for up to 200ms so the first keystrokes after summoning are not lost.
    internal void FocusWhenForeground(IntPtr hwnd, bool selectSearchText)
    {
        var deadline = Environment.TickCount64 + 200;
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            var isForeground = hwnd == IntPtr.Zero || QuickSearchWindowNative.GetForegroundWindow() == hwnd;
            if (!isForeground && Environment.TickCount64 < deadline)
                return;

            timer.Stop();
            _window.TxtSearch.Focus();
            System.Windows.Input.Keyboard.Focus(_window.TxtSearch);
            if (selectSearchText) _window.TxtSearch.SelectAll();
        };
        timer.Start();
    }
}
