using System.IO;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
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

    public string Name => TranslationService.Get("QuickPanel_TabHistory");

    public Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default)
        // Off the calling thread: every entry is checked against the disk, and a history holding paths on
        // a disconnected network drive turns each of those checks into its own timeout.
        => Task.Run<IReadOnlyList<ISearchResult>>(
            () => Build(HistoryService.GetHistoryEntries(), MaxItems, Exists, cancellationToken),
            cancellationToken);

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>The list itself, with the entries and the existence check handed in so it can be tested.</summary>
    internal static List<ISearchResult> Build(
        IEnumerable<HistoryEntry> history, int maxItems, Func<string, bool> exists, CancellationToken cancellationToken = default)
    {
        var entries = new List<ISearchResult>();

        foreach (var entry in history)
        {
            if (cancellationToken.IsCancellationRequested || entries.Count >= maxItems) break;
            if (string.IsNullOrWhiteSpace(entry.Path)) continue;

            // An application's path can be a virtual shell id rather than a file, so there is nothing on
            // disk to check for it. Everything else has to still be there: history outlives the files in
            // it, and a tile that opens nothing is worse than one fewer tile.
            var isApplication = entry.Kind == HistoryEntryKind.Application;
            if (!isApplication && !exists(entry.Path)) continue;

            entries.Add(new PanelResultItem(entry.Path, isApplication: isApplication, modified: OpenedAt(entry.Time)));
        }

        return entries;
    }

    // When it was opened, which is what this list is ordered by. The history arrives newest-first
    // already, so this is really for the timestamp the panel's list view shows beside each row -- and it
    // keeps that order true if a group is re-sorted and put back.
    private static DateTime OpenedAt(long unixSeconds)
        => unixSeconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime : default;
}
