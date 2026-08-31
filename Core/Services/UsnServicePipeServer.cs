using System.IO.Pipes;

using Lertaro.Core.Services.HookLaunch;

using Lertaro.Core.Services.Pipe;

using Lertaro.Core.Services.Search;

using Lertaro.Core.Wire;
namespace Lertaro.Core.Services;

using Lertaro.Core;

public sealed class UsnServicePipeServer : IDisposable
{
    private SearchEngine? _engine;
    private CancellationTokenSource? _pipeCts;

    public void Start(SearchEngine engine)
    {
        _engine = engine;
        _pipeCts = new CancellationTokenSource();
        Task.Run(() => PipeServerLoop(_pipeCts.Token));
    }

    public void Stop()
    {
        _pipeCts?.Cancel();
        _pipeCts?.Dispose();
        _pipeCts = null;
        _engine = null;
    }

    private async Task PipeServerLoop(CancellationToken token)
    {
        Logger.Log("[PipeServer] Pipe server loop started.", LogLevel.Debug);
        var pipeSecurity = PipeSecurityFactory.Create();

        // Pre-create 2 parallel listener loops to serve as a connection pool
        var listeners = Enumerable.Range(0, 2)
            .Select(_ => Task.Run(() => ListenLoopAsync(pipeSecurity, token), token))
            .ToArray();

        await Task.WhenAll(listeners).ConfigureAwait(false);
        Logger.Log("[PipeServer] Pipe server loop stopped.");
    }

    private async Task ListenLoopAsync(PipeSecurity? pipeSecurity, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                if (pipeSecurity != null)
                {
                    pipeServer = NamedPipeServerStreamAcl.Create(
                        "LertaroPipe",
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        65536, 65536,
                        pipeSecurity
                    );
                }
                else
                {
                    pipeServer = new NamedPipeServerStream(
                        "LertaroPipe",
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        65536, 65536
                    );
                }

                await pipeServer.WaitForConnectionAsync(token).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(pipeServer, token), token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                pipeServer?.Dispose();
                Logger.Log($"[PipeServer] Server connection failed: {ex.Message}", LogLevel.Error);
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        using (pipe)
        {
            Logger.Log("[PipeServer] Client connected to pipe.", LogLevel.Debug);

            try
            {
                while (!token.IsCancellationRequested && pipe.IsConnected)
                {
                    var request = await SearchRequestBinarySerializer.ReadSearchRequestAsync(pipe, token);
                    var verboseLog = request.Id != SearchRequestId.Search && request.Id != SearchRequestId.SearchDir;
                    if (verboseLog)
                        Logger.Log($"[PipeServer] Request received: {request.Id}", LogLevel.Debug);

                    if (request.Id is SearchRequestId.Search or SearchRequestId.SearchDir or SearchRequestId.EnumerateDir)
                    {
                        await SearchStreamPump.RunAsync(_engine, request, pipe, token);
                        if (verboseLog)
                            Logger.Log("[PipeServer] Response sent.", LogLevel.Debug);
                        continue;
                    }

                    if (request.Id == SearchRequestId.SubscribeStatus)
                    {
                        await StreamStatusUpdatesAsync(pipe, token).ConfigureAwait(false);
                        continue;
                    }

                    if (request.Id == SearchRequestId.SubscribeDirectoryChanges)
                    {
                        await DirectoryChangeSubscription.ServeAsync(pipe, _engine, request.Directories, token).ConfigureAwait(false);
                        continue;
                    }

                    if (request.Id == SearchRequestId.LaunchHook)
                    {
                        var hookResponse = HookLaunchRequestHandler.Handle(pipe, request.RequestElevation);
                        await WriteControlResponseAsync(pipe, hookResponse, token);
                        if (verboseLog)
                            Logger.Log("[PipeServer] Response sent.", LogLevel.Debug);
                        continue;
                    }

                    if (!pipe.IsConnected)
                    {
                        break;
                    }

                    var response = UsnServicePipeRequestProcessor.Process(_engine, request, token, pipe);

                    if (verboseLog)
                        Logger.Log($"[PipeServer] Sending response: {response.Kind}...", LogLevel.Debug);
                    await WriteControlResponseAsync(pipe, response, token);
                    if (verboseLog)
                        Logger.Log("[PipeServer] Response sent.", LogLevel.Debug);
                }
            }
            catch (Exception ex) when (IsClientDisconnect(ex))
            {
            }
            catch (Exception ex)
            {
                Logger.Log($"[PipeServer] Client connection handler error: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                try
                {
                    GC.Collect(1, GCCollectionMode.Optimized, blocking: false, compacting: false);
                }
                catch { }
            }
        }

        Logger.Log("[PipeServer] Client disconnected from pipe.", LogLevel.Debug);
    }

    private async Task StreamStatusUpdatesAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        // Held in a local for the whole subscription. This loop lives as long as the client stays
        // subscribed and spends nearly all of it parked on the wait below, while Stop() cancels the
        // token and then clears the field -- so by the time the wait resumes on a pool thread, the
        // field is routinely already null. Reading it again after any await, and above all in the
        // finally that every exit path runs through, made shutting the service down terminate it with
        // a NullReferenceException instead. The subscription belongs to the engine it was made on
        // anyway, not to whatever the field happens to hold when it is torn down.
        var engine = _engine;
        if (engine == null)
            return;

        var signal = new SemaphoreSlim(0);

        void Handler(Indexer.Usn.UsnIndexer.IndexerStatus _)
        {
            // Unsubscribing does not wait for a handler already running on the indexer's thread, so
            // one can still arrive between the removal below and the dispose that follows it.
            try { signal.Release(); }
            catch (ObjectDisposedException) { }
        }

        try
        {
            engine.StatusChanged += Handler;
            await PipeResponseBinarySerializer.WriteStatusAsync(pipe, engine.GetStatus(), token).ConfigureAwait(false);

            while (!token.IsCancellationRequested && pipe.IsConnected)
            {
                await signal.WaitAsync(token).ConfigureAwait(false);
                if (!pipe.IsConnected)
                    break;

                await PipeResponseBinarySerializer.WriteStatusAsync(pipe, engine.GetStatus(), token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (IsClientDisconnect(ex) || ex is OperationCanceledException)
        {
        }
        finally
        {
            engine.StatusChanged -= Handler;
            signal.Dispose();
        }
    }

    private static Task WriteControlResponseAsync(Stream stream, PipeResponse response, CancellationToken token) => response.Kind switch
    {
        PipeResponseKind.Ok => PipeResponseBinarySerializer.WriteOkAsync(stream, token),
        PipeResponseKind.Error => PipeResponseBinarySerializer.WriteErrorAsync(stream, response.Message, token),
        PipeResponseKind.Status => PipeResponseBinarySerializer.WriteStatusAsync(stream, response.Status ?? new Indexer.Usn.UsnIndexer.IndexerStatus { State = "error" }, token),
        PipeResponseKind.MachineSettings => PipeResponseBinarySerializer.WriteMachineSettingsAsync(stream, response.MachineSettings ?? new MachineSettings(), token),
        PipeResponseKind.FileMetadata => PipeResponseBinarySerializer.WriteFileMetadataAsync(stream, response.FileMetadata ?? new Dictionary<string, FileMetadataEntry>(), token),
        PipeResponseKind.RecentFiles => RecentFilesResponseCodec.WriteRecentFilesAsync(stream, response.RecentFiles ?? new List<SearchResult>(), token),
        PipeResponseKind.HookLaunched => PipeResponseBinarySerializer.WriteHookLaunchAsync(stream, response.Pid, token),
        PipeResponseKind.SpaceEntries => PipeResponseBinarySerializer.WriteSpaceEntriesAsync(stream, response.SpaceEntries ?? Array.Empty<IndexV2.Space.SpaceIndexEntry>(), token),
        _ => PipeResponseBinarySerializer.WriteErrorAsync(stream, "Unknown response kind", token)
    };

    private static bool IsClientDisconnect(Exception ex) => ex is EndOfStreamException ||
               ex is IOException ||
               ex.InnerException != null && IsClientDisconnect(ex.InnerException);

    public void Dispose() => Stop();
}
