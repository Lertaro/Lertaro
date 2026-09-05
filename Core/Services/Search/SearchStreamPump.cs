using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading.Channels;

using Lertaro.Core.Wire;
using Lertaro.Core.SearchIndex;
namespace Lertaro.Core.Services.Search;

using Lertaro.Core;

public static class SearchStreamPump
{
    // A CLI request adds an App-to-CLI pipe hop, so its consumer can be slower than an index scan.
    // Keeping this bounded applies backpressure instead of retaining every matching result in Service.
    internal const int ResultBufferCapacity = 1024;

    internal static Channel<SearchResult> CreateResultChannel() => Channel.CreateBounded<SearchResult>(
        new BoundedChannelOptions(ResultBufferCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

    public static async Task RunAsync(SearchEngine? engine, SearchRequestMessage msg, Stream stream, CancellationToken token)
    {
        Logger.Log($"[SearchStreamPump] Starting query: '{msg.Query}', limit={msg.Limit}, appLimit={msg.AppLimit}, directoryFilter='{msg.DirectoryFilter}'", LogLevel.Debug);
        using var queryCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var queryToken = queryCts.Token;

        // A long-running search scan (broad/short query over a large index) holds the pipe idle on the
        // server side with no read or write in flight, so a client that gives up and disconnects (types
        // another character, cancelling this request) goes completely unnoticed until this method tries
        // to write the response back -- by then the scan has already run to full completion for nothing.
        // PeekNamedPipe queries the OS connection state directly without consuming/blocking on the data
        // stream, so this can detect that disconnect WHILE the scan is still running and cancel it early.
        using var watchdogStopCts = new CancellationTokenSource();
        _ = WatchForClientDisconnectAsync(stream, queryCts, watchdogStopCts.Token);

        HashSet<byte>? disabledIds = null;
        if (msg.DisabledAliasComponents != null && msg.DisabledAliasComponents.Count > 0)
        {
            disabledIds = new HashSet<byte>();
            foreach (var comp in msg.DisabledAliasComponents)
            {
                var id = AliasProviderRegistry.GetProviderIdByComponentId(comp);
                if (id != 255)
                    disabledIds.Add(id);
            }
        }
        SearchContext.DisabledAliasIds = disabledIds;
        // The service runs as a different identity and cannot read the calling user's settings file,
        // so this preference only exists here as whatever the request carried over the pipe.
        SearchContext.FuzzyMatchEnabled = !msg.ExactMatch;

        var bufferedStream = new BufferedStream(stream, 8192);
        try
        {
            await SearchResponseBinarySerializer.WriteHeaderAsync(bufferedStream, queryToken).ConfigureAwait(false);
            await bufferedStream.FlushAsync(queryToken).ConfigureAwait(false);

            var channel = CreateResultChannel();

            // Set by the enumeration branch below when no loaded drive index holds the requested path,
            // and reported to the client as its own frame after the results drain -- a stream of zero
            // results cannot say the difference between "not indexed" and "that directory is empty",
            // and only the first of those is worth walking the disk over.
            var notIndexed = false;
            var producer = Task.Run(() =>
            {
                try
                {
                    if (msg.Id == SearchRequestId.EnumerateDir)
                    {
                        // Query carries the filename filter here, not a search term (see EnumerateDir).
                        // No engine at all counts as not indexed: the client should fall back, not
                        // conclude the directory is empty.
                        notIndexed = engine == null || !engine.EnumerateDirectory(msg.DirectoryFilter ?? string.Empty,
                            msg.Recursive, msg.Query ?? "*", msg.Limit,
                            result => channel.Writer.WriteAsync(result, queryToken).AsTask().GetAwaiter().GetResult(), queryToken);
                        channel.Writer.TryComplete();
                        return;
                    }

                    var directory = msg.Id == SearchRequestId.SearchDir ? msg.DirectoryFilter : null;

                    engine?.SearchStreaming(msg.Query ?? string.Empty, msg.Limit, msg.AppLimit, directory,
                        result => channel.Writer.WriteAsync(result, queryToken).AsTask().GetAwaiter().GetResult(), queryToken,
                        msg.FileNameFilter);
                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            }, queryToken);

            try
            {
                var count = 0;
                await foreach (var item in channel.Reader.ReadAllAsync(queryToken).ConfigureAwait(false))
                {
                    await SearchResponseBinarySerializer.WriteFileResultAsync(bufferedStream, item, queryToken).ConfigureAwait(false);

                    count++;
                    if (count <= 10 || count % 50 == 0)
                    {
                        await bufferedStream.FlushAsync(queryToken).ConfigureAwait(false);
                    }
                }

                await bufferedStream.FlushAsync(queryToken).ConfigureAwait(false);
                await producer.ConfigureAwait(false);
                if (notIndexed)
                    await SearchResponseBinarySerializer.WriteNotIndexedAsync(bufferedStream, queryToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (IsClientDisconnect(ex))
            {
                queryCts.Cancel();
            }
            catch (Exception ex)
            {
                queryCts.Cancel();
                Logger.Log($"[SearchStreamPump] Error processing streaming search request {msg.Id}: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                try
                {
                    await SearchResponseBinarySerializer.WriteEndAsync(bufferedStream, token).ConfigureAwait(false);
                    await bufferedStream.FlushAsync(token).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsClientDisconnect(ex))
                {
                }
            }
        }
        finally
        {
            try
            {
                bufferedStream.Dispose();
            }
            catch (Exception ex) when (IsClientDisconnect(ex))
            {
            }
            finally
            {
                watchdogStopCts.Cancel();
            }
        }
    }

    // Polls PeekNamedPipe on the raw pipe handle every 25ms and cancels `queryCts` the moment the OS
    // reports the connection is gone -- lets an abandoned scan (see the comment at the call site) abort
    // between chunks instead of always running to completion. No-ops for a non-pipe stream (e.g. tests).
    private static async Task WatchForClientDisconnectAsync(Stream stream, CancellationTokenSource queryCts, CancellationToken stopToken)
    {
        if (stream is not NamedPipeServerStream pipe)
            return;

        var handle = pipe.SafePipeHandle;
        try
        {
            while (!stopToken.IsCancellationRequested)
            {
                await Task.Delay(25, stopToken).ConfigureAwait(false);
                if (handle.IsClosed || handle.IsInvalid)
                    return;

                if (!Win32Api.PeekNamedPipe(handle, IntPtr.Zero, 0, IntPtr.Zero, out _, IntPtr.Zero) &&
                    Marshal.GetLastWin32Error() == Win32Api.ERROR_BROKEN_PIPE)
                {
                    queryCts.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static bool IsClientDisconnect(Exception ex) => ex is EndOfStreamException ||
               ex is IOException ||
               ex.InnerException != null && IsClientDisconnect(ex.InnerException);
}
