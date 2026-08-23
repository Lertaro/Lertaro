using System.Threading;
using System.Windows;
using System.Windows.Input;

namespace Flow.Launcher.Plugin;

/// <summary>
/// Information for font-based glyph icons.
/// </summary>
public record struct GlyphInfo(string FontFamily, string Glyph);

/// <summary>
/// Enumeration of key events for global keyboard hooks.
/// </summary>
public enum KeyEvent
{
    WM_KEYDOWN = 0x0100,
    WM_KEYUP = 0x0101,
    WM_SYSKEYUP = 0x0105,
    WM_SYSKEYDOWN = 0x0104
}

public delegate void FlowLauncherKeyDownEventHandler(FlowLauncherKeyDownEventArgs e);
public delegate void AfterFlowLauncherQueryEventHandler(FlowLauncherQueryEventArgs e);
public delegate void ResultItemDropEventHandler(Result result, IDataObject dropObject, DragEventArgs e);
public delegate bool FlowLauncherGlobalKeyboardEventHandler(int keyevent, int vkcode, SpecialKeyState state);

public delegate void VisibilityChangedEventHandler(object sender, VisibilityChangedEventArgs e);

public class VisibilityChangedEventArgs : EventArgs
{
    public bool IsVisible { get; init; }
}

public class FlowLauncherKeyDownEventArgs
{
    public string Query { get; set; } = string.Empty;
    public KeyEventArgs keyEventArgs { get; set; } = null!;
}

public class FlowLauncherQueryEventArgs
{
    public Query Query { get; set; } = null!;
}

public delegate void ActualApplicationThemeChangedEventHandler(object sender, ActualApplicationThemeChangedEventArgs e);

public class ActualApplicationThemeChangedEventArgs : EventArgs
{
    public bool IsDark { get; init; }
}

public delegate void ResultUpdatedEventHandler(IResultUpdated sender, ResultUpdatedEventArgs e);

public class ResultUpdatedEventArgs : EventArgs
{
    public List<Result> Results = [];
    public Query Query = null!;
    public CancellationToken Token { get; init; } = CancellationToken.None;
}
