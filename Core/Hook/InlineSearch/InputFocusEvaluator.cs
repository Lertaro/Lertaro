using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Core.Hook.InlineSearch;

public static class InputFocusEvaluator
{
    public static bool IsForegroundTextInputFocused(IntPtr foregroundHwnd)
    {
        var threadId = KeyboardNativeMethods.GetWindowThreadProcessId(foregroundHwnd, out _);
        if (threadId == 0)
            return false;

        var info = new KeyboardNativeMethods.GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<KeyboardNativeMethods.GUITHREADINFO>()
        };

        if (!KeyboardNativeMethods.GetGUIThreadInfo(threadId, ref info) || info.hwndFocus == IntPtr.Zero)
            return false;

        var className = new StringBuilder(128);
        if (KeyboardNativeMethods.GetClassName(info.hwndFocus, className, className.Capacity) == 0)
            return false;

        var cls = className.ToString();
        if (cls.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
            cls.Equals("RichEdit20W", StringComparison.OrdinalIgnoreCase) ||
            cls.Equals("RichEdit50W", StringComparison.OrdinalIgnoreCase) ||
            cls.Contains("TextBox", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HasActiveCaret(foregroundHwnd, info);
    }

    private static bool HasActiveCaret(IntPtr foregroundHwnd, KeyboardNativeMethods.GUITHREADINFO info)
    {
        if (info.hwndCaret == IntPtr.Zero)
            return false;

        if (info.rcCaret.Right <= info.rcCaret.Left && info.rcCaret.Bottom <= info.rcCaret.Top)
            return false;

        return info.hwndCaret == foregroundHwnd || KeyboardNativeMethods.IsChild(foregroundHwnd, info.hwndCaret);
    }
}
