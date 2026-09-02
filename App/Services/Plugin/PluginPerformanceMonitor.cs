using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Services.Plugin;

/// <summary>
/// Collects lightweight diagnostics for plugin calls made by the host process.
/// </summary>
public static class PluginPerformanceMonitor
{
    private static readonly ConcurrentDictionary<string, PluginPerformanceCounter> Counters = new(StringComparer.OrdinalIgnoreCase);

    public static T Measure<T>(IPluginComponent component, Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(operation);

        var counter = Counters.GetOrAdd(GetPluginId(component), static _ => new PluginPerformanceCounter());
        var stopwatch = Stopwatch.StartNew();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var threw = false;
        try
        {
            return operation();
        }
        catch
        {
            threw = true;
            counter.Record(stopwatch.ElapsedTicks, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, true);
            throw;
        }
        finally
        {
            if (!threw)
                counter.Record(stopwatch.ElapsedTicks, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, false);
        }
    }

    public static void Measure(IPluginComponent component, Action operation) => Measure(component, () =>
                                                                                     {
                                                                                         operation();
                                                                                         return true;
                                                                                     });

    public static async Task<T> MeasureAsync<T>(IPluginComponent component, Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(operation);

        var counter = Counters.GetOrAdd(GetPluginId(component), static _ => new PluginPerformanceCounter());
        var stopwatch = Stopwatch.StartNew();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var threw = false;
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch
        {
            threw = true;
            counter.Record(stopwatch.ElapsedTicks, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, true);
            throw;
        }
        finally
        {
            if (!threw)
                counter.Record(stopwatch.ElapsedTicks, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, false);
        }
    }

    public static PluginPerformanceSnapshot GetSnapshot(string pluginId) => Counters.TryGetValue(pluginId, out var counter)
            ? counter.Snapshot()
            : PluginPerformanceSnapshot.Empty;

    public static string GetPluginId(IPluginComponent component)
    {
        var assembly = component.GetType().Assembly;
        var location = Path.GetFileName(assembly.Location);
        return string.IsNullOrWhiteSpace(location)
            ? assembly.GetName().Name ?? component.GetType().FullName ?? component.GetType().Name
            : location;
    }

    internal sealed class PluginPerformanceCounter
    {
        private long _invocationCount;
        private long _totalElapsedTicks;
        private long _lastElapsedTicks;
        private long _maxElapsedTicks;
        private long _totalAllocatedBytes;
        private long _exceptionCount;

        public void Record(long elapsedTicks, long allocatedBytes, bool exception)
        {
            Interlocked.Increment(ref _invocationCount);
            Interlocked.Add(ref _totalElapsedTicks, elapsedTicks);
            Interlocked.Exchange(ref _lastElapsedTicks, elapsedTicks);
            InterlockedExtensions.Max(ref _maxElapsedTicks, elapsedTicks);
            Interlocked.Add(ref _totalAllocatedBytes, Math.Max(0, allocatedBytes));
            if (exception)
                Interlocked.Increment(ref _exceptionCount);
        }

        public PluginPerformanceSnapshot Snapshot() => new(
            Interlocked.Read(ref _invocationCount),
            Interlocked.Read(ref _totalElapsedTicks),
            Interlocked.Read(ref _lastElapsedTicks),
            Interlocked.Read(ref _maxElapsedTicks),
            Interlocked.Read(ref _totalAllocatedBytes),
            Interlocked.Read(ref _exceptionCount));
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref long location, long value)
        {
            while (true)
            {
                var current = Interlocked.Read(ref location);
                if (current >= value || Interlocked.CompareExchange(ref location, value, current) == current)
                    return;
            }
        }
    }
}

public readonly record struct PluginPerformanceSnapshot(
    long InvocationCount,
    long TotalElapsedTicks,
    long LastElapsedTicks,
    long MaxElapsedTicks,
    long TotalAllocatedBytes,
    long ExceptionCount)
{
    public static PluginPerformanceSnapshot Empty => default;

    public double AverageElapsedMilliseconds => InvocationCount == 0
        ? 0
        : TotalElapsedTicks * 1000d / Stopwatch.Frequency / InvocationCount;

    public double LastElapsedMilliseconds => LastElapsedTicks * 1000d / Stopwatch.Frequency;
    public double MaxElapsedMilliseconds => MaxElapsedTicks * 1000d / Stopwatch.Frequency;
    public double AllocatedMegabytes => TotalAllocatedBytes / 1024d / 1024d;
}
