using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// The one place a content-index run turns into something the user can see: the cap pause that
/// persists without their action, and the one summary line each finished run leaves behind.
/// </summary>
/// <remarks>
/// Every failure the index hits in passing stays in the log -- a bad document is not news a user can
/// act on. What does reach them is the two things they can act on: the index has stopped because the
/// size cap is reached, and a run has ended. Both are edge-triggered by their callers, so neither can
/// turn into a per-batch or per-file stream of cards.
/// </remarks>
internal static class ContentIndexNotifier
{
    /// <summary>
    /// Tells the user indexing has stopped at the configured size cap. Called once per paused
    /// episode, when the cap is first reached, never again until the index drops back under it.
    /// </summary>
    public static void NotifyIndexCapReached()
    {
        // Kept to two translated pieces so the sentence assembles from whole translated clauses
        // rather than from words reordered by the sender's language.
        Show(
            "ContentSearch_NotificationCapReachedTitle",
            TranslationService.Get("ContentSearch_NotificationCapReachedMessage"),
            NotificationLevel.Warn);
    }

    /// <summary>
    /// Reports the end of one indexing run: completed, interrupted (the plugin stopped) or given up
    /// on (the index cap was reached before the queue drained).
    /// </summary>
    public static void NotifyRunFinished(ContentIndexRunOutcome outcome, int indexedFiles)
    {
        var titleKey = outcome switch
        {
            ContentIndexRunOutcome.Completed => "ContentSearch_NotificationIndexFinishedTitle",
            ContentIndexRunOutcome.Interrupted => "ContentSearch_NotificationIndexStoppedTitle",
            _ => "ContentSearch_NotificationIndexPausedTitle"
        };

        var messageKey = outcome switch
        {
            ContentIndexRunOutcome.Completed => "ContentSearch_NotificationIndexFinishedMessage",
            ContentIndexRunOutcome.Interrupted => "ContentSearch_NotificationIndexStoppedMessage",
            _ => "ContentSearch_NotificationIndexPausedMessage"
        };

        Show(titleKey, TranslationService.Format(messageKey, indexedFiles), NotificationLevel.Info);
    }

    /// <summary>
    /// Translation lookups stay outside the failure guard: a lookup that throws (the host supplies it,
    /// so this plugin does not own it) must not abort a caller mid-index either.
    /// </summary>
    private static void Show(string titleKey, string message, NotificationLevel level)
    {
        try
        {
            PluginNotificationService.Show(new NotificationRequest
            {
                Title = TranslationService.Get(titleKey),
                Message = message,
                Level = level
            });
        }
        catch (Exception ex)
        {
            // The SDK itself never throws; this only covers a host delegate doing so anyway, and it
            // must not surface in the middle of an indexing batch.
            PluginSdk.Logger.Log(
                $"[ContentSearch] Could not show the '{titleKey}' notification: {ex.Message}",
                PluginSdk.LogLevel.Info);
        }
    }
}
