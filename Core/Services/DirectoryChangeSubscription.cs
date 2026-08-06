using System.IO.Pipes;

using Lertaro.Core.Services.Plugin.DirectoryIndex;
using Lertaro.Core.Wire;

namespace Lertaro.Core.Services;

// Serves one SubscribeDirectoryChanges connection: holds that client's watch list, matches every
// applied change batch against it here rather than sending the batches out to be sifted, and writes
// only when one of the watched directories was actually touched.
//
// Its own file rather than another method on UsnServicePipeServer, to keep that one under the project's
// line limit.
internal static class DirectoryChangeSubscription
{
    public static async Task ServeAsync(NamedPipeServerStream pipe, SearchEngine? engine, IReadOnlyList<string>? watched, CancellationToken token)
    {
        if (engine == null)
            return;

        // Copied, because the request message it came from does not outlive this call.
        var watchList = (watched ?? new List<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // An empty watch list is a legitimate subscription -- a client with nothing registered yet --
        // so the connection is held open rather than dropped. It simply never has anything to report
        // until the client resubscribes with a list.
        var signal = new SemaphoreSlim(0);
        var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void OnChanged(string drive, IReadOnlyCollection<string>? changedDirectories)
        {
            var hits = WatchedDirectoryMatcher.Match(watchList, changedDirectories);
            if (hits.Count == 0)
                return;

            lock (pending)
            {
                foreach (var hit in hits)
                    pending.Add(hit);
            }

            // Runs on the indexer's own apply thread, with the next batch typically microseconds away,
            // so this only ever flags and returns -- the write happens on the loop below.
            try { signal.Release(); }
            catch (ObjectDisposedException) { }
        }

        try
        {
            engine.DirectoriesChanged += OnChanged;

            while (!token.IsCancellationRequested && pipe.IsConnected)
            {
                await signal.WaitAsync(token).ConfigureAwait(false);
                if (!pipe.IsConnected)
                    break;

                List<string> hits;
                lock (pending)
                {
                    // Drained as a set, so a burst that flagged the same directory a hundred times
                    // becomes one message naming it once. The semaphore may still be holding counts for
                    // those; the next waits return immediately and find nothing to send, which is
                    // cheaper than trying to keep the two exactly in step.
                    if (pending.Count == 0)
                        continue;
                    hits = pending.ToList();
                    pending.Clear();
                }

                await PipeResponseBinarySerializer.WriteDirectoriesChangedAsync(pipe, hits, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The client went away mid-write; nothing here outlives the connection.
        }
        finally
        {
            engine.DirectoriesChanged -= OnChanged;
            signal.Dispose();
        }
    }
}
