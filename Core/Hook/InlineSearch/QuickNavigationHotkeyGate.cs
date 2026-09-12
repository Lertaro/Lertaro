namespace Lertaro.Core.Hook.InlineSearch;

internal static class QuickNavigationHotkeyGate
{
    internal static bool ShouldSuppress(bool isFileOperationWindow, bool hotkeysDisabled, bool isBlacklisted, bool isFullscreenBlocking) =>
        !isFileOperationWindow && (hotkeysDisabled || isBlacklisted || isFullscreenBlocking);

    internal static bool ShouldSuppress(ExplorerTracker tracker, UserSettings settings, bool hotkeysDisabled, bool isFullscreenBlocking) =>
        ShouldSuppress(
            tracker.IsActiveWindowDialog || tracker.ActiveInlineAdapter?.IsFileExplorer == true,
            hotkeysDisabled,
            ForegroundProcessGate.IsForegroundProcessBlacklisted(settings.BlacklistedProcesses),
            isFullscreenBlocking);
}
