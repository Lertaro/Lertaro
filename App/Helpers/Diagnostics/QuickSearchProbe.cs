using System.Diagnostics;
using System.IO;
using System.Text;

namespace Lertaro.App.Helpers.Diagnostics;

/// <summary>
/// Per-search timing for the quick window: one line per search, one field per stage, so a "it feels laggy"
/// report can be attributed to a stage by measurement instead of by guesswork. Appends to
/// <c>%TEMP%\lertaro-quick-probe.txt</c>.
/// </summary>
/// <remarks>
/// Kept (not a temporary hack) because every latency question in this window starts the same way: which
/// stage got slower. It measures only -- it never changes a decision, an order, or a rendered row -- and it
/// is the read side of the pairing with <see cref="Lertaro.Core.Services.Search.SearchPipelineProbe"/>, which
/// covers the same request inside the service process.
///
/// Stages, all measured from the keystroke. Each is optional on a given line: an inline-window search adds
/// local/merge, the final frame adds final.*, and a query whose results fit in one stream may never emit an
/// intermediate paint.
///   enter         SearchDispatchController.DispatchSearch entered
///   debounce      the keystroke debounce elapsed (30 ms for a multi-character query, 0 for a single one)
///   sent          the request is about to be written to the pipe
///   first         the first result row arrived from the service
///   map.enter     BuildQuickResults entered, with the candidate count it was handed
///   map.files     the file-row loop starts
///   map.rowmax    the slowest single row's match evaluation, plus its name length
///   map.fb        FallbackRank total (rows whose own name did not match)
///   map.create    narrowing app-result construction cost
///   map.filesDone the file-row loop ended
///   map.ranked    ordering finished
///   mapped        mapping and ranking finished
///   local/merge   inline window only: folder snapshot, then the three-tier merge
///   pushed        the results callback returned (the list is on the dispatcher)
///   final.*       the same three stamps for the finishing frame
///   total         the final frame completed
///
/// Reading it: a large enter->sent is the UI thread; sent->first is the pipe plus the service scan (compare
/// with the service probe); first->mapped is mapping and ranking; mapped->pushed is the UI thread again;
/// pushed->total is waiting for the tail plus the second full pass.
///
/// Cost and lifetime: one StringBuilder append per stamp, one synchronous append to the file per search
/// (roughly 0.1 ms). The file is truncated when it passes 4 MB. Set <see cref="Enabled"/> to false to remove
/// even that, which is what a build without a latency question should do.
///
/// The product deliberately writes no ranking log (see dev-docs/search-ranking.md); this is not one -- it
/// records wall-clock durations, never a rank, an order or a reason.
/// </remarks>
internal static class QuickSearchProbe
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();
    private static readonly StringBuilder Stages = new();

    private static long _start;
    private static string _query = string.Empty;
    private static bool _armed;
    private static string _q0 = string.Empty;
    // Search ordinal within this process. The first search after a launch is measurably slower than every
    // later one with the same input, so every record carries its position -- a report can then compare
    // search 1 against search 5 stage by stage instead of guessing which cause is plausible.
    private static int _sequence;
    private static int _current;

    // On, so a latency report can be answered without a rebuild; set LERTARO_SEARCH_PROBE=0 to turn it off.
    //
    // Read straight from the environment instead of through Core's SearchProbeSwitch on purpose. Binding an
    // App-assembly type to a Core type makes an App-only deploy fatal when the installed Core predates that
    // type: the failure is a TypeLoadException inside this class's static initializer, which surfaces as the
    // process dying on the first keystroke rather than as anything that names the real cause. The service
    // probe and the App binary ship together there, so it uses the Core switch.
    public static bool Enabled { get; set; } =
        !string.Equals(Environment.GetEnvironmentVariable("LERTARO_SEARCH_PROBE"), "0", StringComparison.Ordinal);

    public static void Begin(string query)
    {
        if (!Enabled)
            return;

        lock (Gate)
        {
            _query = query;
            _q0 = query.Length == 1 ? query : string.Empty;
            _start = Clock.ElapsedTicks;
            Stages.Clear();
            Stages.Append("n=").Append(_current);
            Stages.Append(" ");
            _current = System.Threading.Interlocked.Increment(ref _sequence);
        _armed = true;
        }
    }

    public static void Mark(string stage, bool withEnvironment = false, string? extra = null)
    {
        if (!Enabled)
            return;

        lock (Gate)
        {
            if (!_armed)
                return;

            Stages.Append(' ').Append(stage).Append('=').Append(Elapsed());
            if (!string.IsNullOrEmpty(extra))
                Stages.Append('(').Append(extra).Append(')');
            if (withEnvironment)
            {
                // A mapping pass that takes 136 ms for ONE row cannot be arithmetic. The two things that make a
                // short CPU-bound pass take a long wall-clock time are a GC pause and a starved thread pool, so
                // both are recorded next to the stamp -- that is what tells those apart from real work.
                Stages.Append("{g0=").Append(GC.CollectionCount(0))
                      .Append(",g2=").Append(GC.CollectionCount(2))
                      .Append(",pauseMs=").Append((long)GC.GetTotalPauseDuration().TotalMilliseconds)
                      .Append(",pending=").Append(ThreadPool.PendingWorkItemCount)
                      .Append(",threads=").Append(ThreadPool.ThreadCount)
                      .Append('}');
            }
        }
    }

    public static void End(int rows)
    {
        if (!Enabled)
            return;

        string line;
        lock (Gate)
        {
            if (!_armed)
                return;
            _armed = false;
            Stages.Append(" rows=").Append(rows);
            // The typed character is recorded only for a ONE-character query, and only because a specific
            // single letter was reported slow -- "z" took 1.1 s of mapping where the others take 6-22 ms.
            // Nothing else is ever written, and this file is TEMP diagnostics.
            line = "qLen=" + _query.Length
                + (_q0.Length > 0 ? " q0=" + _q0 : string.Empty)
                + Stages + " total=" + Elapsed();
        }

        try
        {
            var path = Path.Combine(Path.GetTempPath(), "lertaro-quick-probe.txt");
            var info = new FileInfo(path);
            if (info.Exists && info.Length > 4 * 1024 * 1024)
                File.Delete(path);
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // Never let diagnostics break a search.
        }
    }

    private static string Elapsed()
        => ((Clock.ElapsedTicks - _start) * 1000.0 / Stopwatch.Frequency).ToString("F1");
}
