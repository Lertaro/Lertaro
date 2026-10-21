using System.Security.Cryptography;
using System.Text;

namespace Lertaro.Core.SearchIndex;

/// <summary>One ranked row of the quick window, as far as the calibration log cares about it.</summary>
internal readonly record struct RankingLogRow(string Path, int Rank, int Bucket, int Score);

/// <summary>
/// Calibration log for the ranking model: the leading rows of the quick window's page for each query, plus
/// which row the user actually opened. Every identifying string is a truncated SHA-256 hash, so the file can
/// be analyzed without ever containing a query the user typed or a path they own, and nothing leaves the
/// machine. Design and the three deferred decisions: dev-docs/search-ranking-redesign.md 5.5.
///
/// The question it exists to answer is "which bucket did the opened row sit in", so that the exchange rate
/// and the history caps can be argued from data later instead of by taste.
/// </summary>
internal static class RankingEventLog
{
    /// <summary>How many leading rows of the displayed page are worth recording.</summary>
    internal const int DisplayRowLimit = 10;

    private const long MaxBytes = 10 * 1024 * 1024;
    private const int KeepFiles = 3;
    private const int MaxAgeDays = 30;
    private const string FileName = "ranking-events.jsonl";
    private const string FilePrefix = "ranking-events";

    // A streaming search repaints one query several times a second. When the leading rows are the same as
    // the previous write they carry no new information, so they collapse into one entry. This is a
    // neighbour collapse, not a sampling correction: a page whose top ten genuinely changed still writes.
    private static readonly TimeSpan CollapseWindow = TimeSpan.FromSeconds(5);
    private static readonly UTF8Encoding NoBom = new(false);
    private static readonly object Gate = new();

    /// <summary>Test seam for the settings switch.</summary>
    internal static bool? EnabledOverride;

    private static string? _directoryOverride;

    /// <summary>
    /// Test seam for the data directory. Repointing it is a test-only move, so it also drops the state that
    /// belonged to the previous directory: the collapse signature and the once-per-process age prune.
    /// Production never assigns this.
    /// </summary>
    internal static string? DirectoryOverride
    {
        get => _directoryOverride;
        set
        {
            _directoryOverride = value;
            _lastSignature = string.Empty;
            _lastQueryWriteUtc = DateTime.MinValue;
            _prunedOldFiles = false;
        }
    }

    private static string _lastSignature = string.Empty;
    private static DateTime _lastQueryWriteUtc = DateTime.MinValue;
    private static bool _prunedOldFiles;

    /// <summary>On by default; the settings page exposes the switch (General → System).</summary>
    public static bool IsEnabled => EnabledOverride ?? UserSettings.Load().EnableRankingEventLog;

    /// <summary>Records the ranked page. Never throws and never fails the search it observes.</summary>
    public static void RecordQuery(string query, IReadOnlyList<RankingLogRow> rows)
    {
        if (rows.Count == 0 || !IsEnabled)
            return;

        try
        {
            var taken = Math.Min(rows.Count, DisplayRowLimit);
            var hashes = new string[taken];
            var signature = new StringBuilder();
            for (var i = 0; i < taken; i++)
            {
                hashes[i] = HashKey(rows[i].Path);
                signature.Append(hashes[i]);
            }

            var queryHash = HashKey(query);
            signature.Insert(0, queryHash);
            var signatureText = signature.ToString();

            lock (Gate)
            {
                var now = DateTime.UtcNow;
                if (signatureText == _lastSignature && now - _lastQueryWriteUtc < CollapseWindow)
                    return;

                var lines = new StringBuilder();
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                for (var i = 0; i < taken; i++)
                {
                    var row = rows[i];
                    lines.Append(
                        $$"""{"ts":{{timestamp}},"queryHash":"{{queryHash}}","pathHash":"{{hashes[i]}}","rank":{{row.Rank}},"bucket":{{row.Bucket}},"score":{{row.Score}},"policyVersion":{{RankPolicy.Version}},"opened":false}""").Append('\n');
                }

                Append(lines.ToString());
                _lastSignature = signatureText;
                _lastQueryWriteUtc = now;
            }
        }
        catch
        {
            // ponytail: a calibration log must not be able to break a search, so failures are swallowed and
            // stay invisible. Ceiling: a silently broken log looks exactly like "the user turned it off".
            // The upgrade path is a diagnostics line on the settings page, not a try/catch per search.
        }
    }

    /// <summary>
    /// Records the row the user opened. The position it held is not known here -- the list may have been
    /// re-ranked since -- so an opened line carries no rank/bucket/score and does not take part in the
    /// collapse above.
    /// </summary>
    public static void RecordOpened(string? query, string path)
    {
        if (!IsEnabled)
            return;

        try
        {
            var line =
                $$"""{"ts":{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},"queryHash":"{{(query is null ? string.Empty : HashKey(query))}}","pathHash":"{{HashKey(path)}}","policyVersion":{{RankPolicy.Version}},"opened":true}""" + "\n";

            lock (Gate)
                Append(line);
        }
        catch
        {
            // ponytail: same rule as RecordQuery -- never let the log break the launch it only observes.
        }
    }

    /// <summary>
    /// Truncated SHA-256 of the trimmed, upper-cased text. Case and surrounding whitespace stop
    /// distinguishing an entry from itself, so the same query or path always groups into one hash, and the
    /// original string never reaches the file.
    /// </summary>
    internal static string HashKey(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant())))[..12];

    // Caller holds Gate: the age prune, the size rotation and the append all go through here, so both entry
    // points share one write path.
    private static void Append(string text)
    {
        var directory = DirectoryOverride ?? Logger.UserDataDir;
        Directory.CreateDirectory(directory);
        var main = Path.Combine(directory, FileName);

        if (!_prunedOldFiles)
        {
            _prunedOldFiles = true;
            PruneOldFiles(directory);
        }

        if (File.Exists(main) && new FileInfo(main).Length >= MaxBytes)
            Rotate(directory, main);

        File.AppendAllText(main, text, NoBom);
    }

    private static void PruneOldFiles(string directory)
    {
        var cutoff = DateTime.UtcNow.AddDays(-MaxAgeDays);
        foreach (var file in Directory.EnumerateFiles(directory, FilePrefix + "*.jsonl"))
            if (File.GetLastWriteTimeUtc(file) < cutoff)
                File.Delete(file);
    }

    private static void Rotate(string directory, string main)
    {
        // main -> .1 and .1 -> .2; anything past the kept count is dropped instead of shifted again.
        File.Delete(RolledPath(directory, KeepFiles - 1));
        for (var index = KeepFiles - 2; index >= 1; index--)
        {
            var from = RolledPath(directory, index);
            if (File.Exists(from))
                File.Move(from, RolledPath(directory, index + 1));
        }

        File.Move(main, RolledPath(directory, 1));
    }

    private static string RolledPath(string directory, int index) =>
        Path.Combine(directory, $"{FilePrefix}.{index}.jsonl");
}
