using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.Services.Theme;
using Lertaro.Core;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

/// <summary>
/// Owns the quick window's show sequence so the controller remains focused on state transitions and hide
/// semantics. It keeps a reference to the controller instead of inheriting from it.
/// </summary>
internal sealed class QuickSearchWindowShowSupport
{
    private readonly QuickSearchWindowController _controller;
    private readonly QuickSearchClipboardSupport _clipboardSupport = new();

    internal QuickSearchWindowShowSupport(QuickSearchWindowController controller) => _controller = controller;

    internal void ShowWindow(string? initialQuery, bool caretAtEnd = false)
    {
        _controller.VisibilityOperationToken++;
        PowerThrottlingHelper.WindowShowing("quick");
        IdleWorkingSetTrimmer.WindowShowing();
        ShellOverlayDismissHelper.DismissOverlayIfForeground();

        _controller.LastActiveHwnd = QuickSearchWindowNative.GetForegroundWindow();
        if (_controller.LastActiveHwnd != IntPtr.Zero)
        {
            QuickSearchWindowNative.GetWindowThreadProcessId(_controller.LastActiveHwnd, out var activePid);
            if (activePid == (uint)Environment.ProcessId) _controller.LastActiveHwnd = IntPtr.Zero;
        }

        var window = _controller.Window;
        // (B) The warm-up is over: from here the window is only ever on screen because a summon put it
        // there, so positioning is allowed again (see QuickSearchWindow._suppressPositioning).
        window.AllowPositioning();
        window.ViewModel.IsInlineSearchContext = false;
        App.HideInlineSearch();
        QuickLookManager.Instance.Reset();
        InlineSearchManager.Instance.KeyboardHook.IsQuickSearchWindowVisible = true;
        InlineSearchManager.Instance.KeyboardHook.Stop();
        window.ViewModel.EnsureServiceMonitoringActive();
        var useClipboardText = false;
        var searchQuery = SearchTextPasteFormatter.FormatForSearch(initialQuery) ?? string.Empty;
        if (UserSettings.Load().EnableQuickSearchClipboardAutoFill
            && QuickSearchClipboardSupport.ShouldReadClipboard(initialQuery)
            && _clipboardSupport.TryGetNewText(out var clipboardText))
        {
            useClipboardText = true;
            searchQuery = clipboardText;
        }

        // KeepSearchText: the box may still hold the previous summon's text, because FinishHide skipped
        // wiping it. Leave that text (and its results) in place -- the ActivateAndFocus below selects it
        // all, so the next keystroke replaces it. An explicit query, a clipboard refill, or an already
        // empty box all keep the old behaviour.
        var keepSearchText = ShouldKeepSearchText(UserSettings.Load().SearchWindow.KeepSearchText, searchQuery, window.ViewModel.SearchQuery);
        if (!keepSearchText)
        {
            window.ViewModel.SearchQuery = searchQuery;
            window.ViewModel.RefreshEmptyState();
        }
        window.ViewModel.RefreshLayoutSettings();
        window.UpdateLayout();
        window.ApplyResultsLayoutImmediate();
        window.Topmost = false;
        window.Topmost = true;
        _controller.PositionWindow();

        // No fade-in, deliberately: the whole window is opaque now, and fading means showing the content at
        // zero opacity for the length of the animation, which on an opaque window is a black rectangle
        // rather than a see-through one (NotificationWindowManager reached the same conclusion for cards).
        // What is left of this window's motion is everything that never touches the Window's own opacity.
        window.Show();
        window.WindowState = WindowState.Normal;
        _controller.PositionWindow();

        _controller.ForegroundWatcher.Start();
        // Selecting the kept text is what makes it usable instead of in the way: the next keystroke
        // overwrites it, so the window still reads as a fresh search box.
        _controller.ActivateAndFocus(useClipboardText || keepSearchText, caretAtEnd);

        // The launch panel's sources are read here rather than earlier in this sequence, where they used to
        // be: reading them probes the disk and asks every plugin provider, and on the show path that work
        // was the summon's own latency. Background priority puts it behind the first frame and the focus
        // grab, so the window is up and usable first and the tiles replace the last summon's own.
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Background, new Action(window.ViewModel.RefreshLaunchItems));
    }

    /// <summary>
    /// True when the window reopens showing the text it was dismissed with instead of the query this summon
    /// carries. Only an empty incoming query counts as "no intent" -- an explicit query and a clipboard
    /// refill are both meant to replace the kept text, and an empty box has nothing worth keeping.
    /// </summary>
    /// <remarks>
    /// Both queries are tested with IsNullOrEmpty because this window's query field can still be null on
    /// the very first summon -- nothing has written it yet -- and reading it eagerly is what used to throw
    /// a NullReferenceException here.
    /// </remarks>
    internal static bool ShouldKeepSearchText(bool settingEnabled, string? newQuery, string? existingQuery)
        => settingEnabled && string.IsNullOrEmpty(newQuery) && !string.IsNullOrEmpty(existingQuery);
}
