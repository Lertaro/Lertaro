using System.IO.Pipes;
using System.Threading.Channels;
using Lertaro.Core.Services.Pipe;

using Lertaro.Core.Wire;
namespace Lertaro.Core.Hook.Ipc;

/// <summary>
/// Runs inside the hook process.
/// Connects back to the App's pipe server and sends notifications.
/// Uses two isolated named pipes for physically decoupled Event (Out) and Command (In) streams.
/// </summary>
public sealed class HookIpcServer : IDisposable
{
    private NamedPipeServerStream? _eventPipe;
    private NamedPipeServerStream? _cmdPipe;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private readonly Channel<IpcMessage> _sendChannel;

    // Fired when the App sends us a "STOP" command

    public event Action? OnStopRequested;

    // Fired when the App sends us a custom command

    public event Action<IpcMessage>? OnCommandReceived;

    // Fired once both pipes finish connecting (first connection AND every reconnect) -- after the
    // stale-backlog drain below, so a handler's own SendMessage calls land in the channel where
    // ProcessWriteQueueAsync will actually pick them up instead of getting wiped by that same drain.

    public event Action? OnConnected;

    /// <summary>
    /// The event queue's ceiling. SendMessage is called for every keystroke, mouse click and captured
    /// path, and the only reader runs while an App is connected -- so with no App connected (every App
    /// restart window, and the state a crashed App leaves the hook in) an unbounded queue retained each
    /// event with its heap strings for the rest of the session.
    /// </summary>
    internal const int SendQueueCapacity = 4096;

    /// <summary>
    /// The one send queue, built here so the bound is a property of the channel rather than of a field
    /// initializer nobody can look at.
    /// </summary>
    internal static Channel<IpcMessage> CreateSendChannel() => Channel.CreateBounded<IpcMessage>(
        new BoundedChannelOptions(SendQueueCapacity)
        {
            // Drop the oldest rather than the newest: a queued keystroke notification is worth less than
            // the working set of a process that also holds system-wide hooks, and the events the App
            // cares about are the ones that just happened.
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });

    public HookIpcServer() => _sendChannel = CreateSendChannel();

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ServerLoop(_cts.Token));
    }

    /// <summary>
    /// Sends a binary message to the connected App.
    /// Thread-safe and completely non-blocking: writes to a high-performance Channel.
    /// </summary>
    public void SendMessage(IpcMessage msg) => _sendChannel.Writer.TryWrite(msg);

    public void SendActivate() => SendMessage(new IpcMessage { Id = IpcMessageId.Activate });
    public void SendQuickPanelHotkey() => SendMessage(new IpcMessage { Id = IpcMessageId.QuickPanelHotkey });
    public void SendQuickNavigationHotkey() => SendMessage(new IpcMessage { Id = IpcMessageId.QuickNavigationHotkey });

    private async Task ProcessWriteQueueAsync(NamedPipeServerStream pipe, CancellationTokenSource connectionCts)
    {
        var token = connectionCts.Token;
        try
        {
            var reader = _sendChannel.Reader;
            while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var msg))
                {
                    if (pipe.IsConnected)
                        await PipeRequestBinarySerializer.WriteMessageAsync(pipe, msg, token).ConfigureAwait(false);
                }
            }
        }

        catch (OperationCanceledException) { }

        catch (Exception ex)
        {
            Logger.Log($"[HookIpcServer] Write queue error: {ex.Message}", LogLevel.Warn);
            // Ending the connection is the point of this branch. Returning quietly left the link half
            // alive before: the hook kept reading commands and kept queueing events nothing would ever
            // write, while the App still saw a connected pipe, so hotkeys, path capture and inline search
            // all stopped working with both processes healthy. One write fault (a transient IO error, or
            // ObjectDisposedException from a Dispose racing a reconnect) now costs a reconnect instead.
            try { connectionCts.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    private async Task ServerLoop(CancellationToken token)
    {
        Logger.Log("[HookIpcServer] Starting dual-pipe server loops.", LogLevel.Debug);
        // Current-user-only, not Everyone/AuthenticatedUsers: unlike LertaroPipe (a genuine machine-
        // wide Windows Service meant to serve every logged-in account), each Hook instance is launched
        // per-user into that specific user's own session -- see Services.PipeSecurityFactory.
        // CreateCurrentUserOnly's own comment for why this SID-scoped ACL still works across the
        // elevation boundary.
        var pipeSecurity = PipeSecurityFactory.CreateCurrentUserOnly();
        if (pipeSecurity == null)
        {
            // No unrestricted-pipe fallback here (unlike an earlier version of this method) -- the same
            // rule AppSearchPipeService's own pipe follows (App\Services\AppSearchPipeService.cs): every
            // one of App/Hook/CLI's IPC surfaces is meant to be scoped to its own user in a multi-user
            // environment, so silently widening the ACL the one time SID resolution itself fails isn't an
            // acceptable substitute -- refusing to start is the honest failure mode here too.
            Logger.Log("[HookIpcServer] Could not resolve the current user's SID -- refusing to start.", LogLevel.Error);
            return;
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                var eventPipe = NamedPipeServerStreamAcl.Create(

                    HookIpcNames.EventPipeName,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    4096, 4096,
                    pipeSecurity

                );

                var cmdPipe = NamedPipeServerStreamAcl.Create(

                    HookIpcNames.CmdPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    4096, 4096,
                    pipeSecurity

                );

                Logger.Log("[HookIpcServer] Waiting for App to connect on both pipes...", LogLevel.Debug);

                try
                {
                    await Task.WhenAll(

                        eventPipe.WaitForConnectionAsync(token),
                        cmdPipe.WaitForConnectionAsync(token)

                    ).ConfigureAwait(false);
                }
                catch
                {
                    // The fields the finally block below disposes are only assigned once BOTH sides are
                    // up, so a one-sided failure would abandon two live streams. Each pipe is created
                    // asking for a single server instance, and an abandoned instance keeps its name
                    // occupied -- every later iteration's Create then fails until the finalizer happens
                    // to run, wedging IPC for the life of the hook while the App keeps relaunching it.
                    eventPipe.Dispose();
                    cmdPipe.Dispose();
                    throw;
                }
                Logger.Log("[HookIpcServer] App connected on both pipes.", LogLevel.Debug);
                _eventPipe = eventPipe;
                _cmdPipe = cmdPipe;
                while (_sendChannel.Reader.TryRead(out _)) { }
                OnConnected?.Invoke();
                // One token owns the whole connection: the write pump cancels it when it faults, which is
                // how a dead pump takes the read loop down with it so the loop below reconnects.
                using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(token);

                var writeTask = ProcessWriteQueueAsync(eventPipe, connectionCts);

                try
                {
                    await ListenForCommands(cmdPipe, connectionCts.Token).ConfigureAwait(false);
                }

                finally
                {
                    connectionCts.Cancel();

                    try { await writeTask.ConfigureAwait(false); } catch { }
                }
            }

            catch (OperationCanceledException)
            {
                break;
            }

            catch (Exception ex)
            {
                Logger.Log($"[HookIpcServer] Server loop error: {ex.Message}", LogLevel.Warn);
                await Task.Delay(2000, token).ConfigureAwait(false);
            }

            finally
            {
                try { _eventPipe?.Dispose(); } catch { }

                _eventPipe = null;

                try { _cmdPipe?.Dispose(); } catch { }

                _cmdPipe = null;
            }
        }

        Logger.Log("[HookIpcServer] Server loops stopped.", LogLevel.Debug);
    }

    private async Task ListenForCommands(NamedPipeServerStream pipe, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && pipe.IsConnected)
            {
                var msg = await PipeRequestBinarySerializer.ReadMessageAsync(pipe, token).ConfigureAwait(false);
                Logger.Log($"[HookIpcServer] Received IPC command: {msg.Id}", LogLevel.Debug);
                if (msg.Id == IpcMessageId.Stop)
                {
                    OnStopRequested?.Invoke();
                    return;
                }

                else
                {
                    OnCommandReceived?.Invoke(msg);
                }
            }
        }

        catch (EndOfStreamException) { /* App disconnected */ }

        catch (IOException) { /* App disconnected */ }

        catch (OperationCanceledException) { /* shutting down */ }
    }

    public void Stop() => _cts?.Cancel();

    public void Dispose()
    {
        Stop();

        try { _eventPipe?.Dispose(); } catch { }

        _eventPipe = null;

        try { _cmdPipe?.Dispose(); } catch { }

        _cmdPipe = null;
        _cts?.Dispose();
    }
}
