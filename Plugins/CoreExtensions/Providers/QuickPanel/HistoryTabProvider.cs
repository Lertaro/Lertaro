using System.IO;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.QuickPanel;

/// <summary>Quick panel source backed by this app's own history: what you opened through it.</summary>
/// <remarks>
/// A different list from Windows Recent Items, which the shell fills in whatever application opened the
/// document. This one is only what was reached through Lertaro, and it includes applications, which
/// never reach the shell's list at all.
///
/// Capped at 20, like the startup panel's History tab and for the same reason: there is no settings page
/// of its own to put the number on.
/// </remarks>
public class HistoryTabProvider : IQuickPanelTabProvider
{
    private const int MaxItems = 20;

    // Candidates considered per item the panel will show. The cap is filled from those that survive, so this
    // is only reached when a long run of history points at things that are gone. See BudgetedProbeBatch for
    // the deadline the checks themselves run under.
    private const int CandidateWindowPerItem = 3;

    public string Name => TranslationService.Get("QuickPanel_TabHistory");

    public Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default)
        // Off the calling thread: every entry is checked against the disk, and a history holding paths on
        // a disconnected network drive turns each of those checks into its own timeout.
        => Task.Run<IReadOnlyList<ISearchResult>>(
            () => Build(HistoryService.GetHistoryEntries(), MaxItems, Exists, cancellationToken),
            cancellationToken);

    private static bool Exists(HistoryEntry entry) => entry.Kind switch
    {
        HistoryEntryKind.Application => true,
        HistoryEntryKind.Folder => Directory.Exists(entry.Path),
        _ => File.Exists(entry.Path)
    };

    /// <summary>The list itself, with the entries and the existence check handed in so it can be tested.</summary>
    internal static List<ISearchResult> Build(
        IEnumerable<HistoryEntry> history, int maxItems, Func<HistoryEntry, bool> exists, CancellationToken cancellationToken = default)
    {
        var candidates = new List<HistoryEntry>();
        foreach (var entry in history)
        {
            if (cancellationToken.IsCancellationRequested || candidates.Count >= maxItems * CandidateWindowPerItem) break;
            if (string.IsNullOrWhiteSpace(entry.Path)) continue;
            candidates.Add(entry);
        }

        // One budgeted pass rather than a probe per entry as the list is walked: an entry on a disconnected
        // share used to cost that share's timeout before anything below it was even considered, on the thread
        // the panel was waiting for.
        //
        // An application's path can be a virtual shell id rather than a file, so there is nothing on disk to
        // check for it. Everything else has to still be there: history outlives the files in it, and a tile
        // that opens nothing is worse than one fewer tile.
        var probes = candidates
            .Select(entry => (Func<bool>)(() => entry.Kind == HistoryEntryKind.Application || exists(entry)))
            .ToList();
        var present = BudgetedProbeBatch.Run(probes, false, BudgetedProbeBatch.DefaultBudget);

        var entries = new List<ISearchResult>();
        for (var i = 0; i < candidates.Count && entries.Count < maxItems; i++)
        {
            if (!present[i]) continue;

            var entry = candidates[i];
            var isApplication = entry.Kind == HistoryEntryKind.Application;
            entries.Add(new PanelResultItem(
                entry.Path,
                isApplication: isApplication,
                modified: OpenedAt(entry.Time),
                isDirectory: entry.Kind == HistoryEntryKind.Folder));
        }

        return entries;
    }

    // When it was opened, which is what this list is ordered by. The history arrives newest-first
    // already, so this is really for the timestamp the panel's list view shows beside each row -- and it
    // keeps that order true if a group is re-sorted and put back.
    private static DateTime OpenedAt(long unixSeconds)
        => unixSeconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime : default;
}
