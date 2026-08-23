using System.Threading;

namespace Flow.Launcher.Plugin;

/// <summary>
/// Information for font-based glyph icons.
/// </summary>
public record struct GlyphInfo(string FontFamily, string Glyph);

/// <summary>
/// Virtual key event codes for global keyboard hooks.
/// </summary>
public static class KeyEvent
{
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
}

public delegate void VisibilityChangedEventHandler(object sender, VisibilityChangedEventArgs e);

public class VisibilityChangedEventArgs : EventArgs
{
    public bool IsVisible { get; set; }
}

public delegate void ActualApplicationThemeChangedEventHandler(object sender, ActualApplicationThemeChangedEventArgs e);

public class ActualApplicationThemeChangedEventArgs : EventArgs
{
    public bool IsDark { get; set; }
}

public delegate void ResultUpdatedEventHandler(IResultUpdated sender, ResultUpdatedEventArgs e);

public class ResultUpdatedEventArgs : EventArgs
{
    public List<Result> Results = [];
    public Query Query = null!;
    public CancellationToken Token { get; init; } = CancellationToken.None;
}
