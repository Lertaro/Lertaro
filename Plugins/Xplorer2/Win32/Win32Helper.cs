namespace Lertaro.Plugins.Xplorer2.Win32;

/// <summary>
/// The xplorer2 address-bar protocol: read the active pane's folder, and move it.
///
/// xplorer2 publishes no remote-control interface. Its binary contains no WM_COPYDATA handler, no DDE topic
/// and no IDispatch/OLE object, and its own help says so plainly ("xplorer² doesn't offer a fully scriptable
/// programming model with file and user interface objects"). Its macro language (COMMAND/FINDWND/SETTEXT)
/// only runs inside the process. So unlike the XYplorer adapter -- which sends a script over WM_COPYDATA and
/// gets an answer back -- nothing here is a request/response conversation: it is window reading plus the
/// command numbers xplorer2 documents for its own keyboard, which an outside process can post.
///
/// Split out of one file purely to keep that file under the repo's per-file line limit; the window-structure
/// primitives it operates on live in WindowProbe, which holds no state of its own.
/// </summary>
public static class Win32Helper
{
    // xplorer2's own command numbers, from its keyboard appendix (Table 10) -- the same numbers its
    // "Help > Command finder" dialog shows and that its in-process macro COMMAND verb accepts.
    private const int SwitchToAddressBar = 32859;   // default key: Shift+Tab

    private const uint WM_COMMAND = 0x0111;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const int VK_RETURN = 0x0D;
    private const int VK_ESCAPE = 0x1B;
    private const int SCAN_RETURN = 0x1C;
    private const int SCAN_ESCAPE = 0x01;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// A ComboBox read out of a live frame, with everything the choice of "which one is the address bar"
    /// needs. Carries the fields rather than the HWND so the decision is testable without xplorer2 running.
    /// </summary>
    public readonly struct ComboCandidate
    {
        public ComboCandidate(IntPtr hWnd, bool isVisible, bool parentIsCrumbBar, string text)
        {
            HWnd = hWnd;
            IsVisible = isVisible;
            ParentIsCrumbBar = parentIsCrumbBar;
            Text = text;
        }

        public IntPtr HWnd { get; }
        public bool IsVisible { get; }
        public bool ParentIsCrumbBar { get; }
        public string Text { get; }
    }

    /// <summary>
    /// Picks the combo that currently reports the active pane's folder.
    /// </summary>
    /// <remarks>
    /// Visibility is the discriminator, and it is load-bearing: xplorer2 keeps TWO ComboBoxes in a frame --
    /// the classic address bar (hidden while crumbs are displayed) and the crumb bar's own -- and the hidden
    /// one retains the last path it was given. Measured on a window whose active pane was D:\Notes: its
    /// visible crumb combo reported 'D:\Notes' while its hidden classic combo still reported
    /// 'C:\Windows' from an earlier visit. Reading "the first combo that holds a path" returned the stale
    /// one, so the card searched the wrong folder.
    ///
    /// When no visible combo holds a path the answer is null, not the hidden combo's stale text: a card
    /// scoped to the wrong directory is worse than no card. The crumb bar's own combo wins ties, because in
    /// the default layout it is the one that tracks the focused pane.
    /// </remarks>
    internal static ComboCandidate? PickPathCombo(IEnumerable<ComboCandidate> candidates)
    {
        ComboCandidate? fallback = null;
        foreach (var candidate in candidates)
        {
            if (!candidate.IsVisible) continue;
            if (ChoosePath(new[] { candidate.Text }) == null) continue;
            if (candidate.ParentIsCrumbBar) return candidate;
            fallback ??= candidate;
        }
        return fallback;
    }

    /// <summary>
    /// Finds the address-bar controls of the pane that currently holds focus, plus the path it reports.
    /// </summary>
    public static bool TryFindAddressBar(IntPtr frameHwnd, out IntPtr combo, out IntPtr editor, out string path)
    {
        combo = IntPtr.Zero;
        editor = IntPtr.Zero;
        path = string.Empty;

        var children = WindowProbe.Children(frameHwnd);
        var crumbBar = IntPtr.Zero;
        foreach (var child in children)
        {
            if (!WindowProbe.GetClassName(child).StartsWith("ATL:", StringComparison.OrdinalIgnoreCase)) continue;
            if (WindowProbe.GetText(child).Equals(WindowProbe.CrumbBarText, StringComparison.Ordinal))
                crumbBar = child;
        }

        var candidates = new List<ComboCandidate>();
        foreach (var child in children)
        {
            if (!WindowProbe.GetClassName(child).Equals("ComboBox", StringComparison.OrdinalIgnoreCase)) continue;
            // Read once and reuse: the control reports transient junk while an edit session is open, so a
            // second read could disagree with the first.
            candidates.Add(new ComboCandidate(child, WindowProbe.IsVisible(child),
                WindowProbe.ParentOf(child) == crumbBar, WindowProbe.GetText(child)));
        }

        var picked = PickPathCombo(candidates);
        if (picked == null) return false;

        combo = picked.Value.HWnd;
        path = picked.Value.Text;
        foreach (var grandChild in WindowProbe.Children(combo))
        {
            if (!WindowProbe.GetClassName(grandChild).Equals("Edit", StringComparison.OrdinalIgnoreCase)) continue;
            editor = grandChild;
            break;
        }
        return true;
    }

    /// <summary>
    /// Picks the active pane's path out of texts read from xplorer2's controls and normalises it the way the
    /// rest of the repo expects: no trailing separator unless the path is a drive root.
    /// </summary>
    /// <remarks>
    /// Free of any window call so it can be tested without xplorer2 running. Rejects the transient junk the
    /// address bar reports mid-edit (a bare number) and the shell namespace roots its history keeps (the
    /// "::" CLSID form, e.g. This PC), neither of which is a filesystem path.
    /// </remarks>
    public static string? ChoosePath(IEnumerable<string> candidateTexts)
    {
        foreach (var raw in candidateTexts)
        {
            var text = raw?.Trim() ?? string.Empty;
            if (text.Length == 0) continue;
            if (long.TryParse(text, out _)) continue;
            if (text.StartsWith("::", StringComparison.Ordinal)) continue;
            if (!System.IO.Path.IsPathRooted(text)) continue;
            return text.Length > 3 && text.EndsWith('\\') ? text.TrimEnd('\\') : text;
        }
        return null;
    }

    /// <summary>Gets the active pane's folder path, or null when it cannot be read.</summary>
    public static string? QueryActivePath(IntPtr frameHwnd)
        => StaQueryHelper.Run(() => QueryActivePathCore(frameHwnd), null);

    private static string? QueryActivePathCore(IntPtr frameHwnd)
    {
        if (frameHwnd == IntPtr.Zero) return null;
        return TryFindAddressBar(frameHwnd, out _, out _, out var path) ? ChoosePath(new[] { path }) : null;
    }

    /// <summary>
    /// True when the focused control is one of xplorer2's folder panes -- the only surface inline search
    /// should open over, so typing into its filter box or its mini-scrap bin is left alone.
    /// </summary>
    /// <remarks>
    /// The container test arrives as a seam so the decision is testable without xplorer2 running, the same
    /// shape the Total Commander adapter uses for its quick-rename check. Deliberately not wrapped in
    /// StaQueryHelper: this gates a per-hotkey path, and the real predicate only walks parent links, so it
    /// cannot block on a busy target.
    /// </remarks>
    public static bool IsFileList(IntPtr focusedHwnd, string className) =>
        IsFileListCore(focusedHwnd, className, WindowProbe.IsInsideMillerView);

    internal static bool IsFileListCore(IntPtr focusedHwnd, string className, Func<IntPtr, bool> isInsideMillerView)
    {
        if (focusedHwnd == IntPtr.Zero || string.IsNullOrEmpty(className)) return false;
        return className.Equals(WindowProbe.FileListClass, StringComparison.OrdinalIgnoreCase) && isInsideMillerView(focusedHwnd);
    }

    /// <summary>
    /// Navigates the active pane to <paramref name="path"/> by driving xplorer2's own address bar, the only
    /// externally reachable way in: its list view ignores externally posted selection (measured --
    /// LVM_SETITEMSTATE left the selected count untouched on every pane) and there is no script RPC.
    ///
    /// Sequence, each step measured:
    ///   1. post "Switch to address bar", so xplorer2 opens its own editor and selects the current path.
    ///      Skipping this does not work reliably: writing straight into the edit leaves xplorer2's own
    ///      completion state in charge, and Enter then commits a suggested child folder instead of the text
    ///      that was written (observed -- asking for "C:\Windows" landed on "C:\Windows\appcompat\AIDD").
    ///   2. WM_SETTEXT the requested path, 3. Enter to commit, 4. Escape so focus returns to the pane.
    ///
    /// xplorer2 only acts on a posted command while it is the foreground application -- the same command
    /// number did nothing at all while another window held foreground -- hence stealFocus.
    /// </summary>
    public static bool Navigate(IntPtr frameHwnd, string path, bool stealFocus = true)
        => StaQueryHelper.Run(() => NavigateCore(frameHwnd, path, stealFocus), false);

    private static bool NavigateCore(IntPtr frameHwnd, string path, bool stealFocus)
    {
        if (frameHwnd == IntPtr.Zero || string.IsNullOrEmpty(path)) return false;
        if (!TryFindAddressBar(frameHwnd, out _, out var structuredEditor, out _)) return false;
        if (stealFocus) SetForegroundWindow(frameHwnd);

        PostMessage(frameHwnd, WM_COMMAND, (IntPtr)SwitchToAddressBar, IntPtr.Zero);

        // xplorer2 moves focus into its own editor; prefer that HWND over the one found by structure,
        // because a non-crumb layout can host the editor somewhere else.
        var focused = WindowProbe.GetFocusedControl(frameHwnd);
        var target = PickEditor(focused, WindowProbe.GetClassName(focused), structuredEditor);
        if (target == IntPtr.Zero) return false;
        if (!WindowProbe.SetText(target, path)) return false;

        PostKey(target, VK_RETURN, SCAN_RETURN);
        PostKey(target, VK_ESCAPE, SCAN_ESCAPE);
        return Confirmed(frameHwnd, path);
    }

    /// <summary>
    /// Which control receives the typed path: the editor xplorer2 just focused, if the "Switch to address
    /// bar" command landed on an Edit, otherwise the Edit located under the address bar by structure.
    /// </summary>
    internal static IntPtr PickEditor(IntPtr focusedHwnd, string focusedClassName, IntPtr structuredEditor)
        => focusedClassName.Equals("Edit", StringComparison.OrdinalIgnoreCase) ? focusedHwnd : structuredEditor;

    /// <summary>
    /// Builds the key lParam the way the route was measured working: repeat count 1, the key's own scan code
    /// in bits 16-23, and on release the context and previous-key-state bits set.
    /// </summary>
    private static void PostKey(IntPtr hWnd, int vk, int scan)
    {
        uint down = 1u | ((uint)scan << 16);
        PostMessage(hWnd, WM_KEYDOWN, (IntPtr)vk, (IntPtr)down);
        PostMessage(hWnd, WM_KEYUP, (IntPtr)vk, (IntPtr)unchecked((int)(down | 0xC0000000u)));
    }

    /// <summary>
    /// Reads the pane back, briefly, so a failed hand-off returns false and Lertaro's fallback chain can
    /// still open the result. Bounded well inside the 1s the App waits on the Hook for this call.
    /// </summary>
    private static bool Confirmed(IntPtr frameHwnd, string path)
    {
        var wanted = ChoosePath(new[] { path }) ?? path;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var current = QueryActivePathCore(frameHwnd);
            if (current != null && current.Equals(wanted, StringComparison.OrdinalIgnoreCase)) return true;
            Thread.Sleep(80);
        }
        return false;
    }
}
