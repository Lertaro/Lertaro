using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace Lertaro.Core.Hook.InlineSearch;

internal sealed class QuickNavDoubleClick(Func<nint, int, int, Task<uint?>?> beginHitTest)
{
    private (nint View, int X, int Y, uint Time, Task<uint?>? Background)? _first;

    internal void Clear() => _first = null;

    internal bool Press(nint view, int x, int y, uint time, uint interval, int width, int height)
    {
        if (view == 0)
        {
            Clear();
            return false;
        }
        if (_first is { } first && first.View == view && unchecked(time - first.Time) <= interval
            && Math.Abs((long)x - first.X) * 2 <= width && Math.Abs((long)y - first.Y) * 2 <= height)
        {
            Clear();
            if (first.Background == null) return true;
            if (!first.Background.IsCompletedSuccessfully || first.Background.Result is not uint sampled) return false;
            return unchecked(sampled - first.Time) < unchecked(time - first.Time);
        }
        _first = (view, x, y, time, beginHitTest(view, x, y));
        return false;
    }
}

internal static class ExplorerBackgroundHitTest
{
    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();

    private static int _busy;

    internal static Task<uint?>? Begin(nint view, int x, int y)
    {
        var className = new StringBuilder(128);
        if (ExplorerNativeHooks.GetClassName(view, className, className.Capacity) == 0) return Task.FromResult<uint?>(null);
        if (!className.ToString().Equals("DirectUIHWND", StringComparison.OrdinalIgnoreCase)) return null;
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return Task.FromResult<uint?>(null);
        return Task.Run<uint?>(() =>
        {
            try { return IsFolderView(view) && IsBackground(view, x, y) ? GetTickCount() : null; }
            finally { Volatile.Write(ref _busy, 0); }
        });
    }

    internal static bool IsFolderView(nint view)
    {
        var className = new StringBuilder(128);
        for (var current = view; current != 0; current = ExplorerNativeHooks.GetParent(current))
        {
            if (ExplorerNativeHooks.GetClassName(current, className, className.Capacity) == 0) return false;
            if (className.ToString().Equals("SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    internal static bool IsBackground(nint view, int x, int y)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (element == null) return false;
            var hit = element.Current;
            return hit.ControlType == ControlType.List && hit.NativeWindowHandle == view;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or UnauthorizedAccessException)
        { return false; }
    }
}
