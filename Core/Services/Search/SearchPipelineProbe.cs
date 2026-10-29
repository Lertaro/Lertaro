using System.Diagnostics;
using System.IO;
using System.Text;

namespace Lertaro.Core.Services.Search;

/// <summary>
/// Per-request timing for the service side of a search: one line per stage, tagged with the request id, so a
/// "the first row took 300 ms" report can be split into parse, index dispatch, scan and emit instead of
/// staying one opaque number. Appends to <c>lertaro-svc-probe.txt</c> next to the service binaries.
/// </summary>
/// <remarks>
/// Kept (not a temporary hack) as the read side of the pairing with the App-side
/// <c>QuickSearchProbe</c>: that one measures from the keystroke and sees the pipe only as one opaque gap,
/// this one opens the gap from the inside. Answering "where did the time go" between the two needs no
/// rebuild.
///
/// An event log rather than a single timeline line on purpose: the index layer fans out over drives in
/// parallel, so several stages interleave and a one-line summary would lose that. Read a run by grouping
/// lines on the id and diffing the millisecond stamps.
///
/// A line is <c>id|milliseconds|stage|extra</c>, where the stamp is absolute (a Stopwatch started with the
/// process) so several requests can be laid on one time axis. Stages: pump.enter (request received),
/// engine.enter (engine call starting), coord.enter (index coordinator), coord.single-drive,
/// coord.bounded-fanout (multi-drive buffer-then-sort: the first row waits for the slowest drive),
/// idx.parse-done, idx.read-acquired (index read lock taken, one line per drive), engine.first (first row
/// produced), engine.done, pump.drained (every result handed to the pipe).
///
/// Cost and lifetime: one append per stamp, one synchronous file append per request, truncated at 8 MB. It
/// measures only and never changes a decision or an order. The product deliberately writes no ranking log
/// (see dev-docs/search-ranking.md); this records wall-clock durations, not ranks.
///
/// The service runs as LocalSystem, so <c>Path.GetTempPath()</c> there is not the user's temp directory --
/// which is why this asks for the process name and writes beside the binaries instead. Set
/// <see cref="Enabled"/> to false to remove it from the request path entirely.
/// </remarks>
public static class SearchPipelineProbe
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();
    private static string _id = string.Empty;
    // Request ordinal within this service process. The first search after the service starts is measurably
    // slower than later ones; carrying the position lets a report compare request 1 against request 5 stage
    // by stage instead of reasoning about which cause is plausible.
    private static int _sequence;

    public static bool Enabled { get; set; } = Lertaro.Core.Diagnostics.SearchProbeSwitch.Enabled;

    public static void Begin(string query)
    {
        if (!Enabled)
            return;

        var ordinal = System.Threading.Interlocked.Increment(ref _sequence);
        lock (Gate)
            _id = Guid.NewGuid().ToString("N").Substring(0, 8);

        Write("pump.enter", "qLen=" + query.Length + " n=" + ordinal);
    }

    public static void Write(string stage, string extra = "")
    {
        if (!Enabled)
            return;

        string line;
        lock (Gate)
        {
            var ms = Clock.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
            line = _id + "|" + ms.ToString("F1") + "|" + stage + "|" + extra;
        }

        try
        {
            // Path.GetTempPath() is not usable here: under LocalSystem it resolved to a directory that is
            // neither C:\Windows\Temp nor the systemprofile temp, and every write silently landed nowhere.
            // The service's own install directory is guaranteed writable by the account it runs as, so the
            // file goes next to the binaries; the App side keeps using the user's temp directory.
            var isService = (Environment.ProcessPath ?? string.Empty)
                .EndsWith("Lertaro.Service.exe", StringComparison.OrdinalIgnoreCase);
            var path = isService
                ? Path.Combine(AppContext.BaseDirectory, "lertaro-svc-probe.txt")
                : Path.Combine(Path.GetTempPath(), "lertaro-svc-probe.txt");

            var info = new FileInfo(path);
            if (info.Exists && info.Length > 8 * 1024 * 1024)
                File.Delete(path);
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // Diagnostics must never break a request.
        }
    }
}
