using System.Windows.Input;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// Owns bringing the inline window to the foreground and putting the caret in its search box, including the
// keyboard-layout adoption that goes with it.
//
// Split out purely to keep InlineSearchWindow under the repo's per-file line limit; it holds no state of
// its own beyond the layout it must put back, and always operates on the one window it is given.
internal sealed class InlineSearchFocusSupport
{
    private readonly Lertaro.App.InlineSearchWindow _window;
    private IntPtr _originalLayout = IntPtr.Zero;

    internal InlineSearchFocusSupport(Lertaro.App.InlineSearchWindow window) => _window = window;

    /// <summary>
    /// Focuses the window and its search box, crossing the foreground-lock boundary, and adopts the
    /// keyboard layout of whatever window was in front. Returns whether the box actually ended up focused
    /// and active -- the caller uses that to decide whether keystrokes still need forwarding.
    /// </summary>
    /// <remarks>
    /// The layout adoption is why this needs the window thread ids at all: the panel docks onto another
    /// application's window, and the user expects to keep typing in the input language that window was
    /// using. The original layout is restored on close.
    /// </remarks>
    internal bool ActivateAndFocus()
    {
        _window.ViewModel.EnsureServiceMonitoringActive();
        var foreground = InlineSearchWindowNativeMethods.GetForegroundWindow();
        var currentThread = InlineSearchWindowNativeMethods.GetCurrentThreadId();

        var foregroundThread = foreground != IntPtr.Zero
            ? InlineSearchWindowNativeMethods.GetWindowThreadProcessId(foreground, out _)
            : 0;
        var attached = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
                attached = InlineSearchWindowNativeMethods.AttachThreadInput(currentThread, foregroundThread, true);

            _window.Activate();
            var textBox = _window.SearchBox.SearchTextBox;
            textBox.Focus();
            Keyboard.Focus(textBox);
            textBox.CaretIndex = textBox.Text.Length;

            if (foreground != IntPtr.Zero && foregroundThread != 0)
            {
                _originalLayout = InlineSearchWindowNativeMethods.GetKeyboardLayout(currentThread);
                var layout = InlineSearchWindowNativeMethods.GetKeyboardLayout(foregroundThread);
                if (layout != IntPtr.Zero)
                    InlineSearchWindowNativeMethods.ActivateKeyboardLayout(layout, 0);
            }

            return _window.IsActive && textBox.IsKeyboardFocusWithin;
        }
        finally
        {
            if (attached)
                InlineSearchWindowNativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    /// <summary>Puts back the keyboard layout that was active before the first adoption.</summary>
    internal void RestoreLayout()
    {
        if (_originalLayout == IntPtr.Zero) return;
        InlineSearchWindowNativeMethods.ActivateKeyboardLayout(_originalLayout, 0);
        _originalLayout = IntPtr.Zero;
    }
}
