using System.Runtime.InteropServices;
using System.Windows.Automation;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.WPS.Interop;

/// <summary>
/// Reaches inside WPS's file dialog through UI Automation.
/// </summary>
/// <remarks>
/// Every other file-dialog adapter in this repo walks child HWNDs with FindWindowEx/GetDlgCtrlID. That
/// does not work here: WPS's dialog is built on Qt, which paints its widgets into the one native window
/// rather than giving each a HWND of its own, so a child-window walk finds nothing to address. UI
/// Automation is the only view onto that widget tree, and it is also where the identifying class names
/// ("KcfdFileDialog" and friends) are visible at all -- the Win32 class of the window is a generic
/// "Qt5QWindowIcon".
///
/// The tree this expects:
///
///     KcfdFileDialog                 the dialog
///       KcfdFilterWidget             the file-name row, somewhere below it
///         ...                        (KcfdComboBox in the builds seen so far, but not relied on)
///           QLineEdit                the editor -- or kd::KDTextField, or any Edit control
///
/// Nothing here reads label or caption text: WPS is localized, and the class names are not.
/// </remarks>
internal static class WPSDialogAutomation
{
    /// <summary>How long to keep retrying the editor lookup before giving up.</summary>
    /// <remarks>
    /// The dialog answers UI Automation before its widget tree is fully built, so a single attempt made
    /// the instant the window appears finds the dialog but no editor inside it. Retrying is what the
    /// AutoHotkey implementation this was rebuilt against does, with the same budget.
    /// </remarks>
    internal const int EditorLookupTimeoutMs = 500;

    private const int EditorLookupStepMs = 50;

    /// <summary>
    /// The KcfdFileDialog element for this exact window, or null if this window is not one.
    /// </summary>
    /// <remarks>
    /// Answers only for the handle it is given, and deliberately does not go looking at neighbouring
    /// windows for a dialog. ExplorerWindowClassifier.FindMatchingDialogWindow walks up from the focused
    /// window asking CanHandle at each level and tracks the FIRST handle that says yes, so whatever this
    /// says yes to becomes the window the host treats as the dialog: its rect is what the search bar
    /// docks to, and its destruction is what tells the host the dialog is gone.
    ///
    /// An earlier version searched the owned windows of the handle as well, which made WPS's main window
    /// answer yes on behalf of the dialog hanging off it. The host then tracked the main window -- so the
    /// docked bar attached to that instead of the dialog, and, because the main window is still there
    /// after the dialog closes, it never went away again.
    /// </remarks>
    internal static AutomationElement? GetDialog(IntPtr hwnd)
    {
        var element = TryFromHandle(hwnd);
        if (element == null)
            return null;

        return WPSDialogIdentity.IsWPSDialogClassName(GetClassName(element)) ? element : null;
    }

    /// <summary>
    /// The dialog's file-name editor, or null when it never turned up within
    /// <paramref name="timeoutMs"/>.
    /// </summary>
    /// <remarks>
    /// Searched as a descendant of KcfdFilterWidget rather than by walking a fixed
    /// KcfdFilterWidget -> KcfdComboBox -> editor chain. The intermediate levels differ between WPS
    /// versions, and a walk that hard-codes them finds the dialog but reports no editor on any build that
    /// nests it differently. The class names are still tried first, most specific first, with a plain
    /// Edit control as the last resort so a build that renames its editor class still works.
    ///
    /// Deliberately not cached between calls: an AutomationElement is a handle onto a live UI object and
    /// WPS rebuilds this part of the tree as the user moves between folders and filters, so a cached one
    /// goes stale and then throws on use.
    /// </remarks>
    internal static AutomationElement? FindFileNameEditor(AutomationElement dialog, IntPtr dialogHwnd, int timeoutMs = EditorLookupTimeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (true)
        {
            var editor = FindFileNameEditorOnce(dialog);
            if (editor != null)
                return editor;

            // A dialog the user closed mid-lookup is not going to come back, and retrying against it
            // means aiming another round of cross-process calls at a window that is being torn down.
            if (!WPSWindowInterop.IsAlive(dialogHwnd))
                return null;

            if (Environment.TickCount64 >= deadline)
            {
                Logger.Log("[WPS] no usable file-name editor found in the dialog", LogLevel.Warn);
                return null;
            }

            Thread.Sleep(EditorLookupStepMs);
        }
    }

    private static AutomationElement? FindFileNameEditorOnce(AutomationElement dialog)
    {
        try
        {
            var filterWidget = FindFilterWidget(dialog);
            if (filterWidget == null)
                return null;

            foreach (var condition in EditorConditions())
            {
                var editor = filterWidget.FindFirst(TreeScope.Descendants, condition);
                if (editor != null && IsUsable(editor))
                    return editor;
            }

            return null;
        }
        catch (Exception ex) when (IsTransientAutomationFailure(ex))
        {
            // Not logged: this runs on a retry loop, and the tree being momentarily unavailable is the
            // normal case it exists to ride out. The caller logs once if the whole budget expires.
            return null;
        }
    }

    /// <summary>
    /// Walks down to a widget by its class name without ever entering the file list.
    /// </summary>
    /// <remarks>
    /// This used to be a plain FindFirst(TreeScope.Descendants, KcfdFilterWidget), which was wrong in a
    /// way that only shows up on a real dialog: a descendants search is a subtree walk, KcfdFilterWidget
    /// sits AFTER KcfdContentWidget in the tree, and KcfdContentWidget holds KcfdFileListView -- one node
    /// per file on screen. Reaching the file-name box therefore meant enumerating the entire directory
    /// listing across the process boundary, on every attempt of a retry loop. Nothing wanted the file
    /// list; it was simply in the way.
    ///
    /// So: breadth-first by children only, never descending into a List, and depth-capped. The widgets the
    /// host asks for sit three levels down (dialog -> KcfdAreaSplitter -> KcfdFileDialogContentWidget ->
    /// KcfdFilterWidget / KcfdContentWidget), and the cap leaves room for that to move without letting a
    /// wrong turn become an unbounded walk. The class names are still matched rather than the path being
    /// hard-coded, so a rearranged tree still resolves as long as the widget is somewhere in the first few
    /// levels.
    /// </remarks>
    private static AutomationElement? FindWidgetByClassName(AutomationElement dialog, string className, int maxDepth)
    {
        var walker = TreeWalker.RawViewWalker;
        var queue = new Queue<(AutomationElement Element, int Depth)>();
        queue.Enqueue((dialog, 0));

        while (queue.Count > 0)
        {
            var (element, depth) = queue.Dequeue();
            if (depth >= maxDepth)
                continue;

            var child = walker.GetFirstChild(element);
            while (child != null)
            {
                if (string.Equals(child.Current.ClassName, className, StringComparison.Ordinal))
                    return child;

                if (child.Current.ControlType != ControlType.List)
                    queue.Enqueue((child, depth + 1));

                child = walker.GetNextSibling(child);
            }
        }

        return null;
    }

    private const int MaxWidgetDepth = 6;

    private static AutomationElement? FindFilterWidget(AutomationElement dialog) =>
        FindWidgetByClassName(dialog, WPSDialogIdentity.FilterWidgetClassName, MaxWidgetDepth);

    /// <summary>
    /// The screen bounds of the widget with this class name, from one attempt and without retrying: the
    /// host asks for the card's hang points on the positioning path, which runs again every time the
    /// dialog moves, so a cross-process wait does not belong here.
    /// </summary>
    internal static System.Windows.Rect? TryGetWidgetBounds(IntPtr dialogHwnd, string className)
    {
        var dialog = GetDialog(dialogHwnd);
        if (dialog == null)
            return null;

        try
        {
            var widget = FindWidgetByClassName(dialog, className, MaxWidgetDepth);
            if (widget == null)
                return null;

            return ToScreenBounds(widget);
        }
        catch (Exception ex) when (IsTransientAutomationFailure(ex))
        {
            return null;
        }
    }

    // An empty rect is what a framework reports for an element it lays out but never shows -- and a rect off
    // every screen arrives as NaN, which no cast to int survives usefully. Either way the caller gets "no such
    // widget", which for the card's hang points means keeping the placement it had.
    private static System.Windows.Rect? ToScreenBounds(AutomationElement widget)
    {
        var bounds = widget.Current.BoundingRectangle;
        return bounds.Width <= 0 || bounds.Height <= 0 || double.IsNaN(bounds.X) || double.IsInfinity(bounds.X)
            ? null
            : bounds;
    }

    /// <summary>
    /// The file-name editor's screen bounds, from one attempt.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="FindFileNameEditor"/>'s retry loop: this is asked from the card's geometry
    /// probe on every dialog resize, so sleeping up to <see cref="EditorLookupTimeoutMs"/> here would just
    /// make the answer arrive later than the placement that needs it. A miss costs nothing either -- the
    /// caller keeps whatever it measured last, and a dialog whose widget tree has not answered yet just gets
    /// the placement it always had.
    /// </remarks>
    internal static System.Windows.Rect? TryGetFileNameEditorBounds(IntPtr dialogHwnd)
    {
        var dialog = GetDialog(dialogHwnd);
        if (dialog == null)
            return null;

        var editor = FindFileNameEditorOnce(dialog);
        if (editor == null)
            return null;

        try
        {
            return ToScreenBounds(editor);
        }
        catch (Exception ex) when (IsTransientAutomationFailure(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Whether this dialog's bottom row holds a field a file name can be typed into.
    /// </summary>
    /// <remarks>
    /// One attempt, not <see cref="FindFileNameEditor"/>'s retrying lookup: this is asked from
    /// <see cref="CanHandle"/>, which runs on every foreground change, and it is only ever used to choose
    /// between the dialog's two shapes. A dialog still building its widgets answers "no" here and is then
    /// driven through the address row, which is what the retrying lookup inside the file-name route is for.
    /// </remarks>
    internal static bool HasFileNameEditor(AutomationElement dialog) => FindFileNameEditorOnce(dialog) != null;

    /// <summary>
    /// Navigates by typing the path into the dialog's address row, for the dialogs that have no file-name
    /// field to type into -- WPS's 上传到云 and 导入文件夹 among them, whose bottom row holds nothing but their
    /// own buttons.
    /// </summary>
    /// <remarks>
    /// Measured end to end against a live 上传到云: a posted click in the row's empty strip
    /// (KcfdLocSpaceButton) replaces the whole breadcrumb with a KcfdLocNavigationLineEdit, SetValue puts the
    /// path into that, and a posted Enter navigates the dialog to it.
    ///
    /// The click has to be on the strip rather than anywhere on the row, and the strip is only as wide as
    /// whatever the breadcrumb does not use -- see WPSDialogIdentity.LocationBarClickPoint.
    ///
    /// Focus is confirmed before the keystroke for the same reason the file-name route confirms the
    /// foreground first: an Enter that the field does not take is an Enter the dialog's default button does,
    /// and this dialog's default button is 上传. Backing out with Escape is what keeps a failure here from
    /// leaving a foreign path sitting in the user's address row.
    /// </remarks>
    internal static bool TryNavigateThroughLocationBar(IntPtr dialogHwnd, string path)
    {
        try
        {
            var dialog = GetDialog(dialogHwnd);
            if (dialog == null)
                return false;

            var edit = FindLocationEdit(dialog);
            if (edit == null)
            {
                if (WPSDialogIdentity.LocationBarClickPoint(
                        TryBoundsOf(dialog, WPSDialogIdentity.LocationBarSpaceClassName)) is not { } click)
                    return false;

                WPSWindowInterop.PostClickAtScreenPoint(dialogHwnd, click.X, click.Y);

                // Qt rebuilds the row into its editable form in its own time.
                for (var waited = 0; waited < EditorLookupTimeoutMs && edit == null; waited += EditorLookupStepMs)
                {
                    Thread.Sleep(EditorLookupStepMs);
                    edit = FindLocationEdit(dialog);
                }
            }

            if (edit == null)
                return false;

            if (!TrySetValue(edit, path) || !TryFocus(edit))
            {
                WPSWindowInterop.PostEscapeTo(dialogHwnd);
                return false;
            }

            return WPSWindowInterop.PostEnterTo(dialogHwnd);
        }
        catch (Exception ex) when (IsTransientAutomationFailure(ex))
        {
            return false;
        }
    }

    private static AutomationElement? FindLocationEdit(AutomationElement dialog) =>
        FindWidgetByClassName(dialog, WPSDialogIdentity.LocationEditClassName, MaxWidgetDepth);

    private static System.Windows.Rect? TryBoundsOf(AutomationElement dialog, string className)
    {
        var widget = FindWidgetByClassName(dialog, className, MaxWidgetDepth);
        return widget == null ? null : ToScreenBounds(widget);
    }

    private static IEnumerable<Condition> EditorConditions()
    {
        foreach (var className in WPSDialogIdentity.EditorClassNames)
            yield return new PropertyCondition(AutomationElement.ClassNameProperty, className);

        yield return new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
    }

    private static bool IsUsable(AutomationElement editor)
    {
        try
        {
            return editor.Current.IsEnabled;
        }
        catch (Exception ex) when (IsTransientAutomationFailure(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// Puts <paramref name="text"/> into the editor, and confirms it took.
    /// </summary>
    /// <remarks>
    /// The read-back is not belt-and-braces. SetValue can return without throwing and leave the field
    /// unchanged -- a Qt editor that is read-only, or filtering what it accepts, does exactly that -- and
    /// without this check the caller would go on to press Enter on whatever the field still held, which
    /// in a Save dialog means committing the user's half-typed file name somewhere unexpected.
    /// </remarks>
    internal static bool TrySetValue(AutomationElement editor, string text)
    {
        try
        {
            if (!editor.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) || pattern is not ValuePattern valuePattern)
            {
                Logger.Log("[WPS] the file-name editor does not support ValuePattern", LogLevel.Warn);
                return false;
            }

            valuePattern.SetValue(text);

            if (!string.Equals(valuePattern.Current.Value, text, StringComparison.Ordinal))
            {
                Logger.Log("[WPS] the file-name editor did not accept the path", LogLevel.Warn);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (IsTransientAutomationFailure(ex))
        {
            Logger.Log($"[WPS] failed to set the file-name editor: {ex.GetType().Name}: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    internal static bool TryFocus(AutomationElement element)
    {
        try
        {
            element.SetFocus();
            return true;
        }
        catch (Exception ex) when (IsTransientAutomationFailure(ex))
        {
            return false;
        }
    }

    private static AutomationElement? TryFromHandle(IntPtr hwnd)
    {
        if (!WPSWindowInterop.IsAlive(hwnd))
            return null;

        try
        {
            return AutomationElement.FromHandle(hwnd);
        }
        catch (Exception ex) when (IsTransientAutomationFailure(ex))
        {
            return null;
        }
    }

    private static string? GetClassName(AutomationElement element)
    {
        try
        {
            return element.Current.ClassName;
        }
        catch (Exception ex) when (IsTransientAutomationFailure(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// The failures that mean "this window went away or is not answering", as opposed to a bug here.
    /// </summary>
    /// <remarks>
    /// Every one of these is reachable through no fault of the caller: the user can close the dialog
    /// mid-lookup (ElementNotAvailableException), WPS can be busy long enough for the automation call to
    /// give up (TimeoutException), and a cross-process COM call can fail for either reason underneath
    /// (COMException, InvalidOperationException from a pattern that vanished). This adapter runs inside
    /// the Hook process, which serves every other window integration too, so letting one of these escape
    /// would take that down over a dialog that merely closed too early.
    /// </remarks>
    private static bool IsTransientAutomationFailure(Exception ex)
        => ex is ElementNotAvailableException
            or ElementNotEnabledException
            or TimeoutException
            or COMException
            or InvalidOperationException
            or UnauthorizedAccessException;
}
