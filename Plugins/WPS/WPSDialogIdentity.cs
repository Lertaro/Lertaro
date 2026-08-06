namespace Lertaro.Plugins.WPS;

/// <summary>
/// The name-matching half of recognising a WPS file dialog, kept free of any live window so it can be
/// unit-tested on its own. The parts that need a real window (walking the UI Automation tree, reading
/// the window rect) live in <see cref="Interop.WPSDialogAutomation"/> and
/// <see cref="Interop.WPSWindowInterop"/>.
/// </summary>
internal static class WPSDialogIdentity
{
    /// <summary>
    /// The four executables that put up this dialog: Writer, Spreadsheets, Presentation and the PDF
    /// reader. They are separate processes rather than one host, so all four have to be listed.
    /// </summary>
    private static readonly string[] ProcessNames = { "wps", "et", "wpp", "wpspdf" };

    /// <summary>UI Automation class name of the dialog itself.</summary>
    internal const string DialogClassName = "KcfdFileDialog";

    /// <summary>The container holding the file-name row, a descendant of the dialog.</summary>
    internal const string FilterWidgetClassName = "KcfdFilterWidget";

    /// <summary>
    /// Editor class names seen inside those combo boxes. Two of them because WPS builds vary: older ones
    /// use Qt's own QLineEdit, newer ones a WPS-internal subclass. Whichever is found first is used.
    /// </summary>
    internal static readonly string[] EditorClassNames = { "QLineEdit", "kd::KDTextField" };

    /// <summary>
    /// Whether the owning process is one of WPS's. Compared without the extension because that is what
    /// Process.ProcessName gives the callers (see FileDialogCommandHandler), but a trailing ".exe" is
    /// tolerated so a caller that passes the file name instead still matches.
    /// </summary>
    internal static bool IsWPSProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        var name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        foreach (var candidate in ProcessNames)
        {
            if (name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a UI Automation class name is the dialog's.
    /// </summary>
    /// <remarks>
    /// This is deliberately matched against the class name UI Automation reports, not the one the host
    /// passes into CanHandle. That argument comes from the Win32 GetClassName of the top-level window,
    /// which for these is Qt's own generic "Qt5QWindowIcon" -- shared by WPS's ordinary document windows,
    /// so it distinguishes nothing. "KcfdFileDialog" is the Qt widget's own class, visible only through
    /// automation.
    /// </remarks>
    internal static bool IsWPSDialogClassName(string? automationClassName)
        => string.Equals(automationClassName, DialogClassName, StringComparison.Ordinal);

    /// <summary>
    /// Whether the Win32 window class could belong to the dialog at all.
    /// </summary>
    /// <remarks>
    /// A pre-filter, not an identification: it is here so the authoritative check -- which is a
    /// cross-process UI Automation call -- is never made for a window that plainly cannot be the dialog.
    /// That matters because CanHandle runs whenever the foreground changes, including while the dialog is
    /// being destroyed, and reaching into a window at that moment is the thing to avoid.
    ///
    /// Qt names these windows "Qt5QWindowIcon" (the digits vary with the Qt build, and Sandboxie prefixes
    /// the whole thing with "Sandbox:BoxName:" -- both observed live), so the stable part is the
    /// "QWindowIcon" suffix. WPS's main window is "OpusApp" and its frame is
    /// "KLiteMainWindowShadowBorder"; neither carries it, so both are rejected here for free.
    /// </remarks>
    internal static bool CouldBeDialogWindowClass(string? win32ClassName)
        => !string.IsNullOrEmpty(win32ClassName)
            && win32ClassName.Contains("QWindowIcon", StringComparison.OrdinalIgnoreCase);

}
