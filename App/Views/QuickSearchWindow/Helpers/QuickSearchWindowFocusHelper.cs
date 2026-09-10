using System.Windows.Threading;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

// Split out to keep QuickSearchWindowController below the repository's per-file line limit; this
// helper owns the delayed keyboard-focus handoff used by the one window it receives.
internal sealed class QuickSearchWindowFocusHelper
{
    private readonly Lertaro.App.QuickSearchWindow _window;

    internal QuickSearchWindowFocusHelper(Lertaro.App.QuickSearchWindow window) => _window = window;

    // What the search box's text/caret should look like once it actually has keyboard focus. Kept as a
    // decision separate from the focus call so the precedence is explicit: a clipboard handoff selects
    // everything (so the next keystroke replaces the imported text) and MUST win over a carried-over
    // query, which wants the caret parked after the text so the next keystroke appends to it.
    internal enum SearchBoxFocus { LeaveCaret, SelectAll, CaretAtEnd }

    internal static SearchBoxFocus ResolveSearchBoxFocus(bool selectSearchText, bool caretAtEnd) =>
        selectSearchText ? SearchBoxFocus.SelectAll
        : caretAtEnd ? SearchBoxFocus.CaretAtEnd
        : SearchBoxFocus.LeaveCaret;

    // ForceForeground may complete through the elevated hook process asynchronously. Poll the real
    // foreground window for up to 200ms so the first keystrokes after summoning are not lost.
    internal void FocusWhenForeground(IntPtr hwnd, bool selectSearchText, bool caretAtEnd = false)
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

            switch (ResolveSearchBoxFocus(selectSearchText, caretAtEnd))
            {
                case SearchBoxFocus.SelectAll:
                    _window.TxtSearch.SelectAll();
                    break;
                case SearchBoxFocus.CaretAtEnd:
                    // Mirrors what the full window does with its own initialQuery on load.
                    _window.TxtSearch.CaretIndex = _window.TxtSearch.Text.Length;
                    break;
            }
        };
        timer.Start();
    }
}
