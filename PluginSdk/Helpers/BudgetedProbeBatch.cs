namespace Lertaro.PluginSdk.Helpers;

/// <summary>Runs a batch of disk probes against one shared deadline.</summary>
/// <remarks>
/// File.Exists/Directory.Exists have no timeout of their own, so a single path on a disconnected network
/// share or a removed drive costs seconds by itself. A batch therefore runs under a budget: every probe
/// that answers in time is used, and one that does not counts as "not there", which is the answer a dead
/// share deserves. Probes run in parallel so the ones that do answer do not queue behind a blocked one,
/// and in bounded numbers so a batch of dead paths cannot occupy the whole machine.
///
/// The probes run on dedicated threads rather than the thread pool. They block uncancellably, and the pool
/// answers a blocked worker by injecting replacements at roughly one a second -- so a batch of dead paths
/// would starve every other pool user in the process, which is a worse failure than the slow list this
/// exists to avoid.
///
/// ponytail: an unanswered probe is left to finish on its own thread rather than cancelled, because the
/// Win32 checks take no token. Bounded concurrency is what keeps that to a handful of threads.
/// </remarks>
public static class BudgetedProbeBatch
{
    /// <summary>Long enough for a cold local disk, short enough that a dead share is not waited on.</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromMilliseconds(1500);

    private const int MaxConcurrency = 8;

    /// <param name="probes">The checks to run. Each is expected to block and to be safe to call concurrently.</param>
    /// <param name="unanswered">The answer recorded for any probe that misses the budget or throws.</param>
    /// <param name="budget">How long the whole batch may take. Must not be negative.</param>
    public static T[] Run<T>(IReadOnlyList<Func<T>> probes, T unanswered, TimeSpan budget)
    {
        var answers = new T[probes.Count];
        if (probes.Count == 0) return answers;

        // Written by the probes and copied out once they have been waited for: a probe abandoned at the
        // budget may still finish afterwards, and it must not change the answer its caller was handed.
        var results = new T[probes.Count];
        Array.Fill(results, unanswered);

        var next = -1;
        var threads = new Thread[Math.Min(MaxConcurrency, probes.Count)];
        for (var worker = 0; worker < threads.Length; worker++)
        {
            threads[worker] = new Thread(() =>
            {
                for (var index = Interlocked.Increment(ref next); index < probes.Count; index = Interlocked.Increment(ref next))
                {
                    try { results[index] = probes[index](); }
                    catch { results[index] = unanswered; }
                }
            })
            { IsBackground = true, Name = "LertaroPathProbe" };
        }

        foreach (var thread in threads) thread.Start();

        var deadline = Environment.TickCount64 + (long)budget.TotalMilliseconds;
        foreach (var thread in threads)
        {
            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0) break;
            thread.Join((int)Math.Min(remaining, int.MaxValue));
        }

        Array.Copy(results, answers, probes.Count);
        return answers;
    }
}
