using System.Diagnostics;
using System.Text;

namespace Lertaro.Core.SearchIndex;

/// <summary>
/// Stage timings for the search path, written next to <see cref="RankingEventLog"/>'s own file and behind
/// the same opt-in switch (General → System → ranking log). Where that log answers "why is this row first",
/// this one answers "where did the time go": one line per stage of one search, in both the App and the
/// service.
/// </summary>
/// <remarks>
/// A stage line carries only numbers and fixed stage names -- a duration, a row count, a drive count. The
/// query text and every path stay out of the file, so a trace bundle can be read without exposing what the
/// user searched for. Samples are buffered per search and flushed as one line when the search settles, so
/// the measurement does not itself become the cost it is measuring.
/// </remarks>
public static class PerfTrace
{
    private const string FileName = "perf-events.jsonl";
    private const int KeepFiles = 3;
    private const long MaxFileBytes = 4 * 1024 * 1024;

    private static readonly object Gate = new();
    private static readonly List<string> Pending = new(32);
    private static string? _file;
    private static long _written;
    private static bool _pruned;

    // Test seam, mirroring RankingEventLog.EnabledOverride.
    internal static bool? EnabledOverride;

    /// <summary>
    /// Same switch as the ranking log, plus a marker file the service can honour. The setting lives in the
    /// calling user's profile, which a LocalSystem service cannot read, so without the marker the
    /// service-side stages would be unreachable no matter what the user switched on. Touching
    /// <c>perf-on.marker</c> in a process's own data directory turns tracing on for that process only.
    /// </summary>
    public static bool IsEnabled => EnabledOverride ?? (RankingEventLog.IsEnabled || MarkerPresent());

    private static bool MarkerPresent()
    {
        // Cached for a few seconds: IsEnabled sits on the hot path (every Begin), and an existence check per
        // keystroke is not free. A stale answer only means the trace starts a moment late.
        var now = Environment.TickCount64;
        if (now - _markerCheckedAt < 5_000)
            return _markerFound;

        _markerCheckedAt = now;
        try
        {
            _markerFound = File.Exists(Path.Combine(LogDirectory(), MarkerName));
        }
        catch
        {
            _markerFound = false;
        }

        return _markerFound;
    }

    private static long _markerCheckedAt;
    private static bool _markerFound;

    /// <summary>The marker file's name, in the process's own data directory.</summary>
    public const string MarkerName = "perf-on.marker";

    /// <summary>
    /// A start mark. Returns 0 when tracing is off so the caller pays one branch and nothing else, which is
    /// what lets these calls sit on the hot path.
    /// </summary>
    public static long Begin() => IsEnabled ? Stopwatch.GetTimestamp() : 0;

    /// <summary>Stages of one search, flushed together so the file gets one line per search.</summary>
    public static void Mark(string stage, long startedAt, int rows = -1, string? note = null)
    {
        if (startedAt == 0)
            return;

        var elapsedMs = ElapsedMs(startedAt);
        Append(elapsedMs, stage, rows, note);
    }

    /// <summary>Closes the buffer: one JSON line carrying every stage this search recorded.</summary>
    public static void Flush(string scope, string outcome, long startedAt, int totalRows)
    {
        if (startedAt == 0)
            return;

        var elapsed = ElapsedMs(startedAt);
        lock (Gate)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"at\":\"").Append(DateTimeOffset.UtcNow.ToString("O")).Append('"');
            sb.Append(",\"scope\":\"").Append(scope).Append('"');
            sb.Append(",\"outcome\":\"").Append(outcome).Append('"');
            sb.Append(",\"totalMs\":").Append(elapsed);
            sb.Append(",\"rows\":").Append(totalRows);
            sb.Append(",\"stages\":[");
            for (var i = 0; i < Pending.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(Pending[i]);
            }
            sb.Append("]}");
            Pending.Clear();

            try
            {
                WriteLineLocked(sb.ToString());
            }
            catch
            {
                // A trace that cannot be written must never break a search.
            }
        }
    }

    /// <summary>Drops whatever a cancelled search recorded, so the next one starts clean.</summary>
    public static void Reset()
    {
        lock (Gate)
            Pending.Clear();
    }

    private static void Append(double elapsedMs, string stage, int rows, string? note)
    {
        var sb = new StringBuilder(96);
        sb.Append("{\"s\":\"").Append(stage).Append('"').Append(",\"ms\":").Append(elapsedMs.ToString("0.###"));
        if (rows >= 0)
            sb.Append(",\"rows\":").Append(rows);
        if (!string.IsNullOrEmpty(note))
            sb.Append(",\"n\":\"").Append(note).Append('"');
        sb.Append('}');

        lock (Gate)
        {
            // A search that never reaches Flush (cancelled by the next keystroke) must not pile up.
            if (Pending.Count >= 64)
                Pending.Clear();
            Pending.Add(sb.ToString());
        }
    }

    private static double ElapsedMs(long startedAt)
        => (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency;

    private static void WriteLineLocked(string line)
    {
        _file ??= ResolveFile();
        var bytes = Encoding.UTF8.GetByteCount(line) + 1;
        if (_written + bytes > MaxFileBytes)
        {
            Rotate();
            _file = ResolveFile();
            _written = 0;
        }

        using var stream = new FileStream(_file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.WriteLine(line);
        _written += bytes;
    }

    private static string ResolveFile()
    {
        var dir = LogDirectory();
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, FileName);
    }

    private static string LogDirectory()
    {
        // The same directory the ranking log and every other diagnostic uses, so one bundle holds the whole
        // trace. In the service that resolves to the service's own data directory, which is what keeps the
        // service-side stages out of the App's file.
        return DirectoryOverride ?? Logger.UserDataDir;
    }

    /// <summary>Test seam for the target directory, mirroring RankingEventLog's own.</summary>
    internal static string? DirectoryOverride { get; set; }

    private static void Rotate()
    {
        try
        {
            var dir = LogDirectory();
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var target = Path.Combine(dir, $"perf-events-{stamp}.jsonl");
            if (File.Exists(_file))
                File.Move(_file, target, overwrite: true);

            if (_pruned)
                return;
            _pruned = true;
            var old = new DirectoryInfo(dir)
                .GetFiles("perf-events-*.jsonl")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(KeepFiles);
            foreach (var file in old)
            {
                try { file.Delete(); } catch { }
            }
        }
        catch
        {
            // Rotation is best effort; losing old samples must not stop the search path.
        }
    }
}
