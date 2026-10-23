using System.Security.Cryptography;
using System.Text;

namespace Lertaro.Core.SearchIndex;

/// <summary>Ranking-kernel fields for one candidate, before final page composition.</summary>
internal readonly record struct RankingLogRow(
    string Path,
    int KernelRank,
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
    bool IsApplication,
    RankingCandidateSource Source = RankingCandidateSource.Unknown,
    string? ProviderId = null,
    RankingProviderState ProviderState = RankingProviderState.Unknown,
    MatchEvidence Evidence = default,
    CandidateIdentity Identity = default,
    int? ShadowV3Rank = null,
    int? ShadowV3RankDelta = null);

/// <summary>Writes hashed quick-search page, candidate and open events as JSONL.</summary>
internal static class RankingEventLog
{
    internal const int SchemaVersion = 4;

    private const long MaxBytes = 10 * 1024 * 1024;
    private const int KeepFiles = 3;
    private const int MaxAgeDays = 30;
    private const string FileName = "ranking-events.jsonl";
    private const string FilePrefix = "ranking-events";
    private static readonly TimeSpan CollapseWindow = TimeSpan.FromSeconds(5);
    private static readonly UTF8Encoding NoBom = new(false);
    private static readonly object Gate = new();
    private static readonly string ProcessSalt = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));

    internal static bool? EnabledOverride;

    private static string? _directoryOverride;
    private static string _lastSignature = string.Empty;
    private static DateTime _lastPageWriteUtc = DateTime.MinValue;
    private static RankingPageStamp? _lastPageStamp;
    private static bool _prunedOldFiles;
    private static long _sessionSequence;
    private static long _pageSequence;
    private static long _querySequence;

    internal static string? DirectoryOverride
    {
        get => _directoryOverride;
        set
        {
            _directoryOverride = value;
            _lastSignature = string.Empty;
            _lastPageWriteUtc = DateTime.MinValue;
            _lastPageStamp = null;
            _prunedOldFiles = false;
        }
    }

    public static bool IsEnabled
    {
        get
        {
            try
            {
                return EnabledOverride ?? UserSettings.Load().EnableRankingEventLog;
            }
            catch
            {
                // Diagnostics are fail-open: a corrupt/unavailable setting must not block search.
                return false;
            }
        }
    }

    internal static RankingLogSession BeginSession(
        string? rawQuery,
        string? canonicalQuery,
        RankingTraceWindow window = RankingTraceWindow.Quick,
        int queryVariantFlags = 0) =>
        new(
            $"s-{ProcessSalt}-{Interlocked.Increment(ref _sessionSequence):x}",
            Interlocked.Increment(ref _querySequence),
            window,
            HashKey(rawQuery ?? string.Empty),
            HashKey(canonicalQuery ?? string.Empty),
            queryVariantFlags);

    /// <summary>Writes a composed page and returns its reusable row/open join identity.</summary>
    internal static RankingPageStamp? RecordPage(RankingPageTrace trace)
    {
        if (!IsEnabled)
            return null;

        try
        {
            var displayedHashes = new string[trace.DisplayedRows.Count];
            for (var i = 0; i < trace.DisplayedRows.Count; i++)
                displayedHashes[i] = HashKey(trace.DisplayedRows[i].Path);

            var signatureText = RankingTraceSignature.Compute(trace, displayedHashes);
            lock (Gate)
            {
                var now = DateTime.UtcNow;
                if (signatureText == _lastSignature && now - _lastPageWriteUtc < CollapseWindow)
                    return _lastPageStamp;

                var stamp = new RankingPageStamp(
                    trace.Session.SearchSessionId,
                    $"p-{ProcessSalt}-{Interlocked.Increment(ref _pageSequence):x}",
                    trace.Session.Window,
                    trace.Session.QuerySequence);
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var lines = new StringBuilder();
                lines.Append(RankingEventJsonWriter.PageJson(timestamp, stamp, trace)).Append('\n');
                for (var i = 0; i < trace.DisplayedRows.Count; i++)
                    lines.Append(RankingEventJsonWriter.DisplayedRowJson(timestamp, stamp, trace.Session, displayedHashes[i], trace.DisplayedRows[i])).Append('\n');
                foreach (var candidate in trace.DroppedCandidates)
                    lines.Append(RankingEventJsonWriter.DroppedCandidateJson(timestamp, stamp, trace.Session, candidate)).Append('\n');

                Append(lines.ToString());
                _lastSignature = signatureText;
                _lastPageWriteUtc = now;
                _lastPageStamp = stamp;
                return stamp;
            }
        }
        catch
        {
            // Diagnostics must not block search or opening a result.
            return null;
        }
    }

    /// <summary>Writes a kernel-only page when final composition data is unavailable.</summary>
    public static void RecordQuery(
        string query,
        IReadOnlyList<RankingLogRow> rows,
        IReadOnlyList<string>? typeOrder = null)
    {
        if (!IsEnabled || rows.Count == 0)
            return;

        var session = BeginSession(query, query);
        var displayed = new RankingDisplayedRow[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            displayed[i] = new RankingDisplayedRow(
                row.Path,
                i + 1,
                row.Source,
                RankingSelectionStage.KernelOrder,
                row.ProviderId,
                row.ProviderState,
                row);
        }

        _ = RecordPage(new RankingPageTrace(
            session,
            new RankingPageCounts(rows.Count, rows.Count, rows.Count, rows.Count, 0, 0, rows.Count, rows.Count),
            displayed,
            Array.Empty<RankingDroppedCandidate>(),
            Array.Empty<RankingProviderTrace>(),
            typeOrder,
            IsFinal: true));
    }

    /// <summary>Records an open, joined to the clicked row's page when an impression is available.</summary>
    public static void RecordOpened(string? query, string path, RankingImpression? impression = null)
    {
        if (!IsEnabled)
            return;

        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var builder = new StringBuilder();
            builder.Append("{\"ts\":").Append(timestamp)
                .Append(",\"v\":").Append(SchemaVersion)
                .Append(",\"kind\":\"open\"")
                .Append(",\"queryHash\":\"").Append(query is null ? string.Empty : HashKey(query)).Append('"')
                .Append(",\"pathHash\":\"").Append(HashKey(path)).Append('"');
            if (impression is { } value)
            {
                builder.Append(",\"searchSessionId\":\"").Append(value.Page.SearchSessionId).Append('"')
                    .Append(",\"pageId\":\"").Append(value.Page.PageId).Append('"')
                    .Append(",\"window\":").Append((int)value.Page.Window)
                    .Append(",\"querySeq\":").Append(value.Page.QuerySequence)
                    .Append(",\"finalDisplayedRank\":").Append(value.FinalDisplayedRank)
                    .Append(",\"rank\":").Append(value.FinalDisplayedRank);
            }
            builder.Append(",\"policyVersion\":").Append(RankPolicy.Version)
                .Append(",\"opened\":true}\n");

            lock (Gate)
                Append(builder.ToString());
        }
        catch
        {
            // Diagnostics are fail-open.
        }
    }

    internal static string HashKey(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant())))[..12];

    internal static string HashExact(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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
