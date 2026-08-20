namespace Lertaro.Plugins.AutoCAD;

/// <summary>
/// Identifies AutoCAD's native file dialogs without touching a live window.
/// </summary>
internal static class AutoCADDialogIdentity
{
    public static bool IsAutoCADProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        var normalized = processName.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^4];

        return normalized.Equals("acad", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("acadlt", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCommonDialog(string? className) =>
        string.Equals(className, "#32770", StringComparison.OrdinalIgnoreCase);
}
