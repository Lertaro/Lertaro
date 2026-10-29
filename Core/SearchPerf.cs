using System.Diagnostics;
using System.Reflection;

namespace Lertaro.Core;

// Wall-clock split for one search, emitted at Debug so it only lands in a log somebody is reading on
// purpose. Every call site is per search or per paint -- never per row -- so the cost when the level is
// Info is a handful of Stopwatch reads.
//
// Turn it on: App's 日志级别 = Debug, and ServiceLogLevel = "debug" in machine-settings.json
// (%ProgramData%\Lertaro, or Data\Machine beside a portable copy). Grep the two logs for "[Perf]".
internal static class SearchPerf
{
    public static bool Enabled => Logger.IsEnabled(LogLevel.Debug);

    // Whether the build we are running has the JIT optimiser on. A Debug build (dotnet build without -c
    // Release -- what build_and_run.bat produces) makes every delegate call, property getter and small
    // method in the per-row path several times slower, so perf lines from one are not comparable with the
    // release numbers, or with each other. Printed with the first perf line so a run says what it is.
    public static bool JitOptimized { get; } = ResolveJitOptimized();

    public static string BuildFlavor => JitOptimized ? "release" : "debug";

    private static bool ResolveJitOptimized()
    {
        // This assembly is built in the same configuration as the app and the service that load it, and
        // unlike GetEntryAssembly() it is the same object everywhere (including under a test host).
        var debuggable = typeof(SearchPerf).Assembly.GetCustomAttribute<DebuggableAttribute>();
        return debuggable is null || !debuggable.IsJITOptimizerDisabled;
    }

    public static long Start() => Stopwatch.GetTimestamp();

    public static double Ms(long start) =>
        (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;

    public static double MsBetween(long start, long end) =>
        (end - start) * 1000.0 / Stopwatch.Frequency;

    // For a loop that accumulates many short sections before reporting once.
    public static long ElapsedTicks(long start) => Stopwatch.GetTimestamp() - start;

    public static double MsFromTicks(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    public static void Log(string message)
    {
        if (Enabled)
            Logger.Log("[Perf] " + message, LogLevel.Debug);
    }
}
