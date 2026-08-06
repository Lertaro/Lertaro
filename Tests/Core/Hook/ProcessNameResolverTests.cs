using System.Diagnostics;
using Lertaro.Core.Hook;

namespace Lertaro.Core.Tests.Hook;

/// <summary>
/// Exercised against this very test process, so no external application has to be running. What matters is
/// that the Win32 route agrees exactly with what Process.ProcessName used to return here: plugin adapters
/// compare the result with == against names like "dopus", so a stray ".exe" would silently stop every one
/// of them from matching.
/// </summary>
// The resolver keeps one process-wide cache slot, so these cannot run alongside each other: a concurrent
// test looking up a different id would evict the entry another one is asserting on.
[TestClass]
[DoNotParallelize]
public class ProcessNameResolverTests
{
    private static uint CurrentPid => (uint)Environment.ProcessId;

    [TestMethod]
    public void ReadsTheImagePathOfARunningProcess()
    {
        Assert.IsTrue(ProcessNameResolver.TryGetImagePath(CurrentPid, out var path));
        Assert.IsTrue(Path.IsPathRooted(path), $"expected a full path, got '{path}'");
        Assert.IsTrue(File.Exists(path), $"'{path}' does not exist");
    }

    [TestMethod]
    public void NameMatchesWhatTheManagedProcessApiReports()
    {
        using var current = Process.GetCurrentProcess();
        Assert.AreEqual(current.ProcessName, ProcessNameResolver.GetNameWithoutExtension(CurrentPid));
    }

    [TestMethod]
    public void NameCarriesNoExtension()
    {
        var name = ProcessNameResolver.GetNameWithoutExtension(CurrentPid);
        Assert.IsFalse(name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), $"'{name}' still has its extension");
    }

    [TestMethod]
    public void ReportsFailureForProcessIdZero() =>
        // GetWindowThreadProcessId yields 0 for a dead or invalid window, and the callers pass that straight
        // through -- it must not be treated as a real process id.
        Assert.IsFalse(ProcessNameResolver.TryGetImagePath(0, out _));

    [TestMethod]
    public void FallsBackToTheGivenValueForAProcessThatIsNotRunning()
    {
        var pid = FindUnusedProcessId();
        Assert.AreEqual("Unknown", ProcessNameResolver.GetNameWithoutExtension(pid));
        Assert.AreEqual("none", ProcessNameResolver.GetNameWithoutExtension(pid, "none"));
    }

    [TestMethod]
    public void DefaultsToUnknownSoCallersNeverSeeNull() =>
        // ExplorerTracker.GetProcessName has always answered "Unknown" rather than null or empty; adapters
        // null-check the value but the tracker's own logging does not.
        Assert.AreEqual("Unknown", ProcessNameResolver.GetNameWithoutExtension(FindUnusedProcessId()));

    [TestMethod]
    public void RepeatedLookupsOfTheSameProcessHitTheCache()
    {
        // The point of the cache: this runs per keystroke outside a text box and per system-wide window
        // event, and consecutive calls are almost always about the same process.
        var lookups = 0;
        Func<uint, string> counting = _ => { lookups++; return @"C:\Windows\explorer.exe"; };

        for (var i = 0; i < 5; i++)
            Assert.IsTrue(ProcessNameResolver.TryGetImagePathCore(0xF0000001, counting, out _));

        Assert.AreEqual(1, lookups);
    }

    [TestMethod]
    public void ADifferentProcessIsLookedUpAgain()
    {
        var lookups = 0;
        Func<uint, string> counting = _ => { lookups++; return @"C:\Windows\explorer.exe"; };

        ProcessNameResolver.TryGetImagePathCore(0xF0000002, counting, out _);
        ProcessNameResolver.TryGetImagePathCore(0xF0000003, counting, out _);
        ProcessNameResolver.TryGetImagePathCore(0xF0000002, counting, out _);

        Assert.AreEqual(3, lookups, "the slot holds one process, so alternating ids must not report hits");
    }

    [TestMethod]
    public void AFailedLookupIsNotCached()
    {
        // Caching "this process has no name" would keep answering that after a transient failure.
        var lookups = 0;
        Func<uint, string> failing = _ => { lookups++; return string.Empty; };

        Assert.IsFalse(ProcessNameResolver.TryGetImagePathCore(0xF0000004, failing, out _));
        Assert.IsFalse(ProcessNameResolver.TryGetImagePathCore(0xF0000004, failing, out _));

        Assert.AreEqual(2, lookups);
    }

    [TestMethod]
    public void ACachedHitReturnsThePathItStored()
    {
        var lookups = 0;
        ProcessNameResolver.TryGetImagePathCore(0xF0000005, _ => { lookups++; return @"C:\Program Files\thing\app.exe"; }, out _);
        ProcessNameResolver.TryGetImagePathCore(0xF0000005, _ => { lookups++; return @"D:\somewhere\else.exe"; }, out var path);

        Assert.AreEqual(@"C:\Program Files\thing\app.exe", path);
        Assert.AreEqual(1, lookups);
    }

    [TestMethod]
    public void ProcessIdZeroIsNeverLookedUp()
    {
        var lookups = 0;
        ProcessNameResolver.TryGetImagePathCore(0, _ => { lookups++; return @"C:\Windows\explorer.exe"; }, out _);

        Assert.AreEqual(0, lookups);
    }

    // Process ids are multiples of 4 on Windows; walk down from an implausibly high one until we find a
    // value no live process owns, rather than hard-coding an id that might just happen to be in use.
    private static uint FindUnusedProcessId()
    {
        var live = Process.GetProcesses();
        try
        {
            var taken = new HashSet<int>(live.Select(p => p.Id));
            for (var candidate = 0x7FFF_FFF0; candidate > 0; candidate -= 4)
            {
                if (!taken.Contains(candidate))
                    return (uint)candidate;
            }
        }
        finally
        {
            foreach (var p in live) p.Dispose();
        }

        Assert.Fail("could not find an unused process id");
        return 0;
    }
}
