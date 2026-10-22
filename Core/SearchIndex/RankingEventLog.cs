using System.Security.Cryptography;
using System.Text;

namespace Lertaro.Core.SearchIndex;

/// <summary>
/// One ranked row of the quick window, as far as the calibration log cares about it. The fields after
/// <see cref="Score"/> decompose the key that placed the row: the section and score say where it landed,
/// and the type rank, index-rule priority, history tier and match shape say why. Every one of them is a
/// number, so the schema reads the same whatever language the user searches in.
/// </summary>
internal readonly record struct RankingLogRow(
    string Path,
    int Rank,
    RankSection Section,
    int Score,
    int TypeRank,
    IndexRuleEffect Priority,
    HistoryTier Tier,
    int HistoryTie,
    MatchRank Match,
    bool IsFavorite,
    bool IsHistoryOnly,
    bool IsDir,
    bool IsApplication);

/// <summary>
/// Calibration log for the ranking model: a page line per query, the leading rows of the quick window's
/// page, and the row the user actually opened. Every identifying string is a truncated SHA-256 hash and
/// every other field is a number or a small enum, so the file can be analyzed without ever containing a
/// query the user typed, a path they own or a file name. A row says "a 31-character name, four levels down,
/// .lnk, strong whole-word match, type rank 0" instead of naming it. Nothing leaves the machine. Design and
/// the deferred decisions: dev-docs/search-ranking-redesign.md 5.5.
///
/// It answers two questions: which bucket the opened row sat in, so the exchange rate and the history caps
/// can be argued from data later instead of by taste, and why one row beat another when the user says the
/// order is wrong -- the decomposition is what makes that answerable without the user's text.
/// </summary>
internal static class RankingEventLog
{
    /// <summary>
    /// The line schema. Bump it when a field is added, removed or redefined, so a reader can tell two
    /// shapes apart; the ranking policy version is separate and stays in <see cref="RankPolicy.Version"/>.
    /// </summary>
    internal const int SchemaVersion = 2;

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
    /// <param name="typeOrder">The quick window's resolved result-type order, as provider type ids. Those
    /// are the application's own type names, not anything the user typed, and they are what maps a row's
    /// <c>typeRank</c> back to a type without shipping the settings file.</param>
    public static void RecordQuery(
        string query,
        IReadOnlyList<RankingLogRow> rows,
        IReadOnlyList<string>? typeOrder = null)
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
                lines.Append(
                    $$"""{"ts":{{timestamp}},"v":{{SchemaVersion}},"kind":"page","queryHash":"{{queryHash}}","rows":{{rows.Count}},"shown":{{taken}},"order":[{{OrderJson(typeOrder)}}],"policyVersion":{{RankPolicy.Version}}}""").Append('\n');

                for (var i = 0; i < taken; i++)
                    lines.Append(RowJson(timestamp, queryHash, hashes[i], rows[i])).Append('\n');

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

    // One row, as numbers. The match is flattened to its five components instead of serialized as an
    // object: a reader recomputing Score needs them separately, and separate fields keep old lines
    // parseable with a plain JSON reader that has no schema.
    private static string RowJson(long timestamp, string queryHash, string pathHash, RankingLogRow row) =>
        $$"""{"ts":{{timestamp}},"v":{{SchemaVersion}},"kind":"row","queryHash":"{{queryHash}}","pathHash":"{{pathHash}}","rank":{{row.Rank}},"bucket":{{(int)row.Section}},"score":{{row.Score}},"typeRank":{{row.TypeRank}},"prio":{{PriorityWeight(row.Priority)}},"hTier":{{(int)row.Tier}},"histTie":{{row.HistoryTie}},"fav":{{Flag(row.IsFavorite)}},"histOnly":{{Flag(row.IsHistoryOnly)}},"strong":{{Flag(row.Match.IsStrong)}},"mTier":{{row.Match.Tier}},"mShape":{{row.Match.Shape}},"mStart":{{row.Match.Start}},"mCov":{{Permille(row.Match.Coverage)}},"mCont":{{Permille(row.Match.Contiguity)}},"dir":{{Flag(row.IsDir)}},"app":{{Flag(row.IsApplication)}},"nameLen":{{NameLength(row.Path)}},"depth":{{Depth(row.Path)}},"ext":{{ExtensionClass(row.Path)}},"policyVersion":{{RankPolicy.Version}},"opened":false}""";

    /// <summary>The priority as the score sees it: high 2, normal 1, low 0.</summary>
    private static int PriorityWeight(IndexRuleEffect effect) => effect switch
    {
        IndexRuleEffect.High => 2,
        IndexRuleEffect.Normal => 1,
        _ => 0,
    };

    private static int Flag(bool value) => value ? 1 : 0;

    /// <summary>Coverage and contiguity arrive as 0..1 ratios; permille keeps them integral and readable.</summary>
    private static int Permille(double value) =>
        (int)Math.Round(Math.Clamp(value, 0, 1) * 1000, MidpointRounding.AwayFromZero);

    private static int NameLength(string path) => Path.GetFileName(path).Length;

    private static int Depth(string path) => path.Count(c => c is '\\' or '/') + 1;

    /// <summary>
    /// The extension as a small enum: 0 none (or a directory), 1 .lnk, 2 .exe, 3 .url, 4 .appref-ms,
    /// 5 anything else. One integer is enough to tell a shortcut from a plain file, and it cannot leak an
    /// unusual extension as text.
    /// </summary>
    private static int ExtensionClass(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Length == 0)
            return 0;
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase))
            return 4;
        return 5;
    }

    private static string OrderJson(IReadOnlyList<string>? typeOrder)
    {
        if (typeOrder is null || typeOrder.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        for (var i = 0; i < typeOrder.Count; i++)
        {
            if (i > 0)
                builder.Append(',');
            builder.Append('"').Append(Escape(typeOrder[i])).Append('"');
        }

        return builder.ToString();
    }

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

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
                $$"""{"ts":{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},"v":{{SchemaVersion}},"kind":"open","queryHash":"{{(query is null ? string.Empty : HashKey(query))}}","pathHash":"{{HashKey(path)}}","policyVersion":{{RankPolicy.Version}},"opened":true}""" + "\n";

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
