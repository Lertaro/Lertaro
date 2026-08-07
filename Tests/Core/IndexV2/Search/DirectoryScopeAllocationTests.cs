using Lertaro.Core.IndexV2.Search;
using System.Diagnostics;

namespace Lertaro.Core.Tests.IndexV2.Search;

[TestClass]
public sealed class DirectoryScopeAllocationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void SearchStreaming_ResolvedDirectoryScope_AvoidsPerResultPathAllocations()
    {
        using var fixture = BuildDrive(fileCount: 10_000);

        Search(fixture, null); // Warm JIT and thread-static search scratch state.
        var unscopedBytes = MeasureAllocatedBytes(() => Search(fixture, null));
        var scopedBytes = MeasureAllocatedBytes(() => Search(fixture, @"C:\Scope"));
        var unscopedTime = MeasureMedianElapsed(() => Search(fixture, null));
        var scopedTime = MeasureMedianElapsed(() => Search(fixture, @"C:\Scope"));

        TestContext.WriteLine($"Unscoped: {unscopedBytes:N0} B; scoped: {scopedBytes:N0} B; delta: {scopedBytes - unscopedBytes:N0} B.");
        TestContext.WriteLine($"Median unscoped: {unscopedTime.TotalMilliseconds:N2} ms; scoped: {scopedTime.TotalMilliseconds:N2} ms.");

        Assert.IsLessThanOrEqualTo(unscopedBytes + 100_000L, scopedBytes,
            "A resolved directory scope must not rebuild one full path per matched row.");
        // The scoped search must preserve the same visible result count as the unscoped fixture.
        Assert.AreEqual(10, Search(fixture, @"C:\Scope"));
    }

    private static long MeasureAllocatedBytes(Func<int> action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static int Search(LiveIndexFixture fixture, string? directoryFilter)
    {
        var count = 0;
        IndexV2Searcher.SearchStreaming(fixture.Index, "item", 10, _ => count++, CancellationToken.None, directoryFilter);
        return count;
    }

    private static TimeSpan MeasureMedianElapsed(Func<int> action)
    {
        var samples = new long[7];
        for (var i = 0; i < samples.Length; i++)
        {
            var start = Stopwatch.GetTimestamp();
            _ = action();
            samples[i] = Stopwatch.GetTimestamp() - start;
        }
        Array.Sort(samples);
        return TimeSpan.FromSeconds(samples[samples.Length / 2] / (double)Stopwatch.Frequency);
    }

    private static LiveIndexFixture BuildDrive(int fileCount)
    {
        var records = new List<FileRecord>(fileCount + 2)
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "Scope", FileRecordFlags.Directory),
        };
        for (var i = 0; i < fileCount; i++)
            records.Add(new FileRecord((UInt128)(i + 3), 2, $"item{i:D5}.txt", FileRecordFlags.None));
        return LiveIndexFixture.Build("C", records);
    }
}
