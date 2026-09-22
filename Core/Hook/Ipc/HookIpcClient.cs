using System.Diagnostics;
using System.IO.Pipes;
using Lertaro.Core.Services.Search;
using Lertaro.Core.Wire;
using Lertaro.Core.Hook.Commands;
namespace Lertaro.Core.Hook.Ipc;
public sealed class HookIpcClient : IDisposable
{
    private Process? _hookProcess;
    private readonly HookLaunchBroker _launchBroker = new();
    private NamedPipeClientStream? _eventPipe;
    private NamedPipeClientStream? _cmdPipe;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    public int ServiceProcessId { get; private set; }
    // False during cold-start or hook downtime, so IPC-bound calls fail fast instead of waiting for an unreachable reply.
    public bool IsConnected => _cmdPipe != null && _cmdPipe.IsConnected;
    private bool _isHotkeysDisabled;

    public bool IsHotkeysDisabled
    {
        get => _isHotkeysDisabled;

        set
        {
            if (_isHotkeysDisabled != value)
            {
                _isHotkeysDisabled = value;
                SendMessage(new IpcMessage { Id = IpcMessageId.SetHotkeysDisabled, BoolVal = value });
            }
        }
    }

    private async Task ClosePipesAsync()
    {
        try
        {
            await _writeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                _eventPipe?.Dispose(); _eventPipe = null;
                _cmdPipe?.Dispose(); _cmdPipe = null;
            }
            finally { _writeGate.Release(); }
        }
        catch (ObjectDisposedException) { }
    }

    public event Action? OnActivated;
    public event Action? OnQuickPanelHotkey;
    public event Action? OnQuickNavigationHotkey;
    public event Action<char>? OnCharacterTyped;
    public event Action? OnBackspacePressed;
    public event Action? OnEscapePressed;
    public event Action? OnEnterPressed;
    public event Action? OnUpPressed;
    public event Action? OnDownPressed;
    public event Action? OnLeftPressed;
    public event Action? OnRightPressed;
    public event Action<int>? OnCtrlNumberPressed;
    public event Action? OnFocusInlineSearchRequested;
    public event Action<int, int>? OnMouseClick;
    public event Action<int, int>? OnMouseDoubleClick;
    public event Action<int, int>? OnMouseMiddleClick;
    public event Action<IntPtr, string, string, bool>? OnExplorerActivated;
    public event Action? OnExplorerDeactivated;
    public event Action<string, bool, bool>? OnPathCaptured;
    public event Action<IReadOnlyList<string>>? OnOpenedFoldersCaptured;
    public event Action? OnActiveWindowMoved;
    public event Action<string>? OnError;
    public HookIpcClient() { }

    public void Start()
    {
        if (_cts != null) return; // already started
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => RunLoop(_cts.Token));
    }
    public void Stop()
    {
        _cts?.Cancel();
        SendMessage(new IpcMessage { Id = IpcMessageId.Stop });
        _ = ClosePipesAsync();
    }
    public void SendMessage(IpcMessage msg) => _ = SendMessageAsync(msg);

    /// <summary>
    /// Sends a command and reports whether the write actually reached the hook. That is all either side can
    /// know of a fire-and-forget command -- there is no reply channel -- but it is enough to tell "the hook
    /// was told" from "the command never left this process", which is the difference a user-facing action
    /// needs before it claims success.
    /// </summary>
    public Task<bool> TrySendMessageAsync(IpcMessage msg) => SendMessageAsync(msg);

    /// <summary>
    /// Commands that leave a state the hook keeps applying until it is told otherwise. Losing one is not
    /// "a dropped write": the hook carries the previous value, and for the inline-visibility flags that
    /// value is what stops Escape, Backspace and the arrows reaching the foreground application. They are
    /// remembered and re-sent after every successful connect, which is what the hotkey-disabled flag
    /// already did by hand.
    /// </summary>
    private static readonly HashSet<IpcMessageId> StickyStates =
    [
        IpcMessageId.SetAppProcessId,
        IpcMessageId.SetHotkeysDisabled,
        IpcMessageId.SetQuickSearchVisible,
        IpcMessageId.SetInlineSearchVisible,
        IpcMessageId.SetInlineWindowOnScreen,
    ];

    private readonly Dictionary<IpcMessageId, IpcMessage> _pendingState = new();

    private async Task<bool> SendMessageAsync(IpcMessage msg)
    {
        // False until a write is confirmed, so a pipe that died mid-write still reports not sent.
        var sent = false;
        try
        {
            await _writeGate.WaitAsync().ConfigureAwait(false);

            try
            {
                if (_cmdPipe is { IsConnected: true } cmdPipe)
                {
                    await PipeRequestBinarySerializer.WriteMessageAsync(cmdPipe, msg).ConfigureAwait(false);
                    sent = true;
                }

                if (StickyStates.Contains(msg.Id))
                {
                    _pendingState[msg.Id] = msg;
                    if (!sent)
                        Logger.Log($"[HookIpcClient] {msg.Id} could not reach the hook; re-sent on connect.", LogLevel.Debug);
                }
                else if (!sent)
                {
                    Logger.Log($"[HookIpcClient] Dropped {msg.Id}: the hook pipe is not connected.", LogLevel.Debug);
                }
            }

            finally
            {
                _writeGate.Release();
            }
        }

        catch (Exception ex)
        {
            Logger.Log($"[HookIpcClient] Failed to send IPC message {msg.Id}: {ex.Message}", LogLevel.Warn);
        }

        return sent;
    }

    /// <summary>
    /// Replays the remembered state onto a freshly connected hook, so a command lost during the
    /// reconnect window cannot leave the new connection carrying a stale view of the App's windows.
    /// Caller holds <see cref="_writeGate"/>.
    /// </summary>
    private async Task ResendPendingStateAsync()
    {
        foreach (var state in _pendingState.Values.ToList())
        {
            if (_cmdPipe is not { IsConnected: true })
                return;

            await PipeRequestBinarySerializer.WriteMessageAsync(_cmdPipe, state).ConfigureAwait(false);
        }
    }
    private async Task RunLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                _hookProcess = await LaunchHookProcessAsync(token).ConfigureAwait(false);
                if (_hookProcess == null)
                {
                    // Warn, not Error: expected on a cold start while the Service is still coming up --
                    // this loop retries every 5s and self-heals once it's reachable. Inside the
                    // cold-start window it is Debug outright so a boot does not log a batch of these.
                    var level = ServicePipeReadinessGate.Instance.IsColdStart(Environment.TickCount64)
                        ? LogLevel.Debug
                        : LogLevel.Warn;
                    Logger.Log("[HookIpcClient] Failed to launch hook process.", level);
                    await Task.Delay(5000, token);
                    continue;
                }

                ServiceProcessId = _hookProcess.Id;
                Logger.Log($"[HookIpcClient] Hook process launched (PID {_hookProcess.Id}), connecting to Event and Cmd pipes...", LogLevel.Debug);
                await Task.Delay(500, token);
                using var eventPipe = new NamedPipeClientStream(".", HookIpcNames.EventPipeName, PipeDirection.In, PipeOptions.Asynchronous);
                using var cmdPipe = new NamedPipeClientStream(".", HookIpcNames.CmdPipeName, PipeDirection.Out, PipeOptions.Asynchronous);

                await Task.WhenAll(

                    eventPipe.ConnectAsync(5000, token),
                    cmdPipe.ConnectAsync(5000, token)

                ).ConfigureAwait(false);
                Logger.Log("[HookIpcClient] Connected to hook pipes.", LogLevel.Debug);

                // Connected is not the same as connected to *our* hook: the pipe name is derived from the
                // user's identity and session rather than being secret, so a process that answered the
                // name first would otherwise go on driving this app's Explorer/inline-search state and
                // hand it tool-run requests to execute.
                var serverPid = HookPipePeer.TryGetServerProcessId(eventPipe);
                if (HookPipePeer.IsImpersonation(serverPid, _hookProcess.Id))
                {
                    throw new UnauthorizedAccessException(
                        $"Hook pipe is served by PID {serverPid}, expected {_hookProcess.Id}.");
                }

                // Only now do the streams become the ones SendMessageAsync writes to. Publishing them
                // before ConnectAsync -- as this used to -- let IsConnected be consulted on pipes that
                // were not connected yet, which is how a state command sent in that window disappeared
                // without a word.
                await _writeGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    _eventPipe = eventPipe;
                    _cmdPipe = cmdPipe;
                    await ResendPendingStateAsync().ConfigureAwait(false);
                }

                finally
                {
                    _writeGate.Release();
                }

                // Send initial process ID of the App so the Service can ignore it.
                SendMessage(new IpcMessage { Id = IpcMessageId.SetAppProcessId, ProcessId = (uint)Environment.ProcessId });
                SendMessage(new IpcMessage { Id = IpcMessageId.SetHotkeysDisabled, BoolVal = _isHotkeysDisabled });
                // Listen for events from Hook Service.
                while (!token.IsCancellationRequested && eventPipe.IsConnected && !_hookProcess.HasExited)
                {
                    var msg = await PipeRequestBinarySerializer.ReadMessageAsync(eventPipe, token).ConfigureAwait(false);
                    DispatchEvent(msg);
                }
            }

            catch (OperationCanceledException)
            {
                break;
            }

            catch (TimeoutException)
            {
                Logger.Log("[HookIpcClient] Timeout connecting to hook pipe; will retry.", LogLevel.Warn);
            }

            catch (EndOfStreamException)
            {
                Logger.Log("[HookIpcClient] Hook process disconnected (EOF); will restart.", LogLevel.Warn);
            }

            catch (IOException ex)
            {
                Logger.Log($"[HookIpcClient] Pipe IO error: {ex.Message}; will restart.", LogLevel.Warn);
            }

            catch (UnauthorizedAccessException ex)
            {
                // Not a transient fault to retry quietly: a process other than the hook we launched owns
                // the pipe name. Logged at Error so it is visible, and the finally below tears the
                // attempt down so the loop can race for the name again with a genuine hook. The
                // impostor itself is another process's to terminate, not ours.
                Logger.Log($"[HookIpcClient] {ex.Message} Relaunching the hook.", LogLevel.Error);
            }

            catch (Exception ex)
            {
                Logger.Log($"[HookIpcClient] Unexpected error: {ex.Message}; will restart.", LogLevel.Warn);
            }

            finally
            {
                await _writeGate.WaitAsync().ConfigureAwait(false);

                try
                {
                    _eventPipe = null;
                    _cmdPipe = null;
                }

                finally
                {
                    _writeGate.Release();
                }

                try { _hookProcess?.Kill(); } catch { }

                _hookProcess = null;
            }

            if (!token.IsCancellationRequested)
            {
                await Task.Delay(2000, token).ConfigureAwait(false);
            }
        }

        Logger.Log("[HookIpcClient] Loop exited.", LogLevel.Debug);
    }

    private void DispatchEvent(IpcMessage msg)
    {
        try
        {
            switch (msg.Id)
            {
                case IpcMessageId.Activate: OnActivated?.Invoke(); break;
                case IpcMessageId.QuickPanelHotkey: OnQuickPanelHotkey?.Invoke(); break;
                case IpcMessageId.QuickNavigationHotkey: OnQuickNavigationHotkey?.Invoke(); break;
                case IpcMessageId.KeyBackspace: OnBackspacePressed?.Invoke(); break;
                case IpcMessageId.KeyEscape: OnEscapePressed?.Invoke(); break;
                case IpcMessageId.KeyEnter: OnEnterPressed?.Invoke(); break;
                case IpcMessageId.KeyUp: OnUpPressed?.Invoke(); break;
                case IpcMessageId.KeyDown: OnDownPressed?.Invoke(); break;
                case IpcMessageId.KeyLeft: OnLeftPressed?.Invoke(); break;
                case IpcMessageId.KeyRight: OnRightPressed?.Invoke(); break;
                case IpcMessageId.ExplorerDeactivated: OnExplorerDeactivated?.Invoke(); break;
                case IpcMessageId.ActiveWindowMoved: OnActiveWindowMoved?.Invoke(); break;
                case IpcMessageId.KeyChar:
                    OnCharacterTyped?.Invoke(msg.CharVal);
                    break;
                case IpcMessageId.KeyCtrlNumber:
                    OnCtrlNumberPressed?.Invoke(msg.IntVal);
                    break;
                case IpcMessageId.FocusInlineSearch:
                    OnFocusInlineSearchRequested?.Invoke();
                    break;
                case IpcMessageId.MouseClick:
                    OnMouseClick?.Invoke(msg.MouseX, msg.MouseY);
                    break;
                case IpcMessageId.MouseDoubleClick:
                    OnMouseDoubleClick?.Invoke(msg.MouseX, msg.MouseY);
                    break;

                case IpcMessageId.MouseMiddleClick:
                    OnMouseMiddleClick?.Invoke(msg.MouseX, msg.MouseY);
                    break;

                case IpcMessageId.ExplorerActivated:
                    OnExplorerActivated?.Invoke(new IntPtr(msg.Hwnd), msg.StringVal1 ?? string.Empty, msg.StringVal2 ?? string.Empty, msg.IsDesktop);
                    break;

                case IpcMessageId.PathCaptured:
                    OnPathCaptured?.Invoke(msg.StringVal1 ?? string.Empty, msg.IsDesktop, msg.IsDialog);
                    break;

                case IpcMessageId.OpenedFoldersCaptured:
                    OnOpenedFoldersCaptured?.Invoke(msg.StringList ?? Array.Empty<string>());
                    break;

                case IpcMessageId.Error:
                    OnError?.Invoke(msg.StringVal1 ?? string.Empty);
                    break;

                case IpcMessageId.ExecuteInlineItemResponse:
                    InlineAdapterIpcCoordinator.SetExecuteItemResult(msg.IntVal, msg.BoolVal);
                    break;

                case IpcMessageId.RunTool:
                    // Off this thread: the tool is a separate process and the App must keep answering
                    // pings while it runs, exactly like the Hook's own snapshot build.
                    _ = Task.Run(() => AppToolRunner.Run(msg.StringVal1 ?? string.Empty, msg.StringVal2 ?? string.Empty, SendMessage));
                    break;
            }
        }

        catch (Exception ex)
        {
            Logger.Log($"[HookIpcClient] Error dispatching IPC message {msg.Id}: {ex.Message}", LogLevel.Warn);
        }
    }

    // Always asks for elevation -- the Service only actually grants it when this session's user is
    // genuinely an administrator (see HookProcessBroker), so there's nothing left for the App to decide.
    private Task<Process?> LaunchHookProcessAsync(CancellationToken token) =>
        _launchBroker.LaunchAsync(requestElevation: true, token);

    public void Dispose()
    {
        Stop();
        // Cancel without Dispose: the receive loop still holds this token and registers on it while
        // unwinding (a disposed CTS throws ObjectDisposedException there; it holds no unmanaged
        // resources, so skipping Dispose is safe).
        _cts?.Cancel();
        _cts = null;
        _launchBroker.Dispose();
    }
}
