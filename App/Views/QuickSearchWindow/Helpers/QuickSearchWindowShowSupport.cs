using System.Windows;
using System.Windows.Media.Animation;
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

    internal void ShowWindow(string? initialQuery)
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
        window.ViewModel.IsInlineSearchContext = false;
        App.HideInlineSearch();
        QuickLookManager.Instance.Reset();
        window.ViewModel.RefreshLaunchItems();
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

        window.ViewModel.SearchQuery = searchQuery;
        window.ViewModel.RefreshEmptyState();
        window.ViewModel.RefreshLayoutSettings();
        window.UpdateLayout();
        window.ApplyResultsLayoutImmediate();
        window.Topmost = false;
        window.Topmost = true;
        _controller.PositionWindow();

        var fadeContent = window.Content as UIElement;
        fadeContent?.BeginAnimation(UIElement.OpacityProperty, null);
        fadeContent?.Opacity = 0;
        window.Show();
        window.WindowState = WindowState.Normal;

        if (fadeContent != null)
        {
            var targetOpacity = ThemeManager.Instance.ActiveTheme?.WindowOpacity ?? 1.0;
            var fadeIn = new DoubleAnimation(targetOpacity, (Duration)System.Windows.Application.Current.FindResource("DurationWindowFadeIn"))
            {
                EasingFunction = System.Windows.Application.Current.TryFindResource("EaseOutCubic") as IEasingFunction
            };
            fadeContent.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        _controller.ForegroundWatcher.Start();
        _controller.ActivateAndFocus(useClipboardText);
    }
}
