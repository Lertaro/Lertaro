using System.IO;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.QuickPanel;

/// <summary>Quick panel source backed by Windows' own recent-documents list.</summary>
/// <remarks>
/// The shell drops a shortcut into %AppData%\Microsoft\Windows\Recent every time a document is opened in
/// any application, whether or not this app was involved. That makes it a different list from the app's
/// own history, and the one people mean by "what was I just working on".
///
/// The shortcuts are resolved to the files they point at, rather than being shown as the .lnk files they
/// are. That costs a COM call per entry, which is why the cap is applied first: only the newest handful
/// are ever resolved. It buys the real name, the real folder and, in a panel that shows thumbnail tiles
/// by default, the file's own thumbnail instead of a shortcut icon for every entry alike.
/// </remarks>
public class WindowsRecentTabProvider : IQuickPanelTabProvider
{
    // Enough to fill a panel that is a quarter of a window, and few enough that resolving every one of
    // them is not something the user waits on. Fixed rather than configurable for the same reason the
    // startup panel's History tab is: there is no settings page of its own to put the number on.
    private const int MaxItems = 30;

    // Enough candidates to survive a long run of entries whose targets are gone, and few enough that a
    // Recent folder which has accumulated thousands of stale shortcuts is not resolved in full to produce
    // the same handful of rows. See BudgetedProbeBatch for the other half of the budget.
    private const int CandidateWindowPerItem = 3;

    public string Name => TranslationService.Get("QuickPanel_TabWindowsRecent");

    public Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default)
        // Off the calling thread: this reads a directory and then resolves shortcuts through COM, and the
        // panel asks for it while it is being summoned.
        => Task.Run<IReadOnlyList<ISearchResult>>(
            () => Build(
                Environment.GetFolderPath(Environment.SpecialFolder.Recent),
                MaxItems,
                StartMenuShortcutResolver.ResolveShortcutTarget,
                cancellationToken),
            cancellationToken);

    /// <summary>The list itself, with the folder and the shortcut resolver handed in so it can be tested.</summary>
    internal static List<ISearchResult> Build(
        string recentFolder, int maxItems, Func<string, string?> resolve, CancellationToken cancellationToken = default)
    {
        var entries = new List<ISearchResult>();
        if (maxItems <= 0 || string.IsNullOrWhiteSpace(recentFolder) || !Directory.Exists(recentFolder)) return entries;

        FileInfo[] shortcuts;
        try
        {
            shortcuts = new DirectoryInfo(recentFolder).GetFiles("*.lnk");
        }
        catch (Exception ex)
        {
            Logger.Log($"[WindowsRecentTabProvider] Cannot read {recentFolder}: {ex.Message}", LogLevel.Warn);
            return entries;
        }

        // Newest first before the cap, not after: the cap is meant to keep the most recent entries, and
        // the directory hands them over in whatever order it likes.
        Array.Sort(shortcuts, (left, right) => right.LastWriteTime.CompareTo(left.LastWriteTime));

        var window = Math.Min(shortcuts.Length, maxItems * CandidateWindowPerItem);

        // Resolving reads the shortcut itself, which is local and cheap; whether what it points at is still
        // there is the part that can block. The two are separated so only the second half needs a budget.
        var targets = new string?[window];
        var probes = new List<Func<TargetKind>>(window);
        for (var i = 0; i < window && !cancellationToken.IsCancellationRequested; i++)
        {
            try
            {
                targets[i] = resolve(shortcuts[i].FullName);
            }
            catch (Exception ex)
            {
                Logger.Log($"[WindowsRecentTabProvider] Cannot resolve {shortcuts[i].Name}: {ex.Message}", LogLevel.Warn);
            }

            var target = targets[i];
            probes.Add(() => Probe(target));
        }

        var kinds = BudgetedProbeBatch.Run(probes, TargetKind.Missing, BudgetedProbeBatch.DefaultBudget);

        // Two shortcuts can resolve to the same file: opening it from two apps, or the shell rewriting an
        // entry rather than replacing it. Only the newest of them is worth a tile.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < probes.Count && entries.Count < maxItems; i++)
        {
            var target = targets[i];
            // A shortcut to something deleted, renamed or on a drive that is not plugged in. The shell
            // leaves those behind for weeks, and a tile that opens nothing is worse than one fewer tile.
            if (kinds[i] == TargetKind.Missing || string.IsNullOrWhiteSpace(target)) continue;
            if (!seen.Add(target!)) continue;

            // The shortcut's own write time, not the file's: this list is ordered by when things were
            // last opened, which is exactly what the shell records here, and a document read but not
            // changed has a modified time from long before.
            //
            // isDirectory is handed over rather than left for PanelResultItem to probe again: the kind just
            // came back from the probe above, and re-asking would be a second blocking call per entry.
            entries.Add(new PanelResultItem(target!, modified: shortcuts[i].LastWriteTime,
                isDirectory: kinds[i] == TargetKind.Directory));
        }

        return entries;
    }

    /// <summary>What a shortcut's target turned out to be. None of these three checks is cancellable.</summary>
    private enum TargetKind { Missing, File, Directory }

    private static TargetKind Probe(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return TargetKind.Missing;
        if (Directory.Exists(target)) return TargetKind.Directory;
        return File.Exists(target) ? TargetKind.File : TargetKind.Missing;
    }
}
