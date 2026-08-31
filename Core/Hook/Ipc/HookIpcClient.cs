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
    public event Action<char>? OnCharacterTyped;
    public event Action? OnBackspacePressed;
    public event Action? OnEscapePressed;
    public event Action? OnEnterPressed;
    public event Action? OnUpPressed;
    public event Action? OnDownPressed;
    public event Action? OnLeftPressed;
    public event Action? OnRightPressed;
    public event Action<int>? OnCtrlNumberPressed;
    public event Action<int, int>? OnMouseClick;
    public event Action<int, int>? OnMouseDoubleClick;
    public event Action<int, int>? OnMouseMiddleClick;
    public event Action<IntPtr, string, string, bool>? OnExplorerActivated;
    public event Action? OnExplorerDeactivated;
    public event Action<string, bool>? OnPathCaptured;
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

    private async Task SendMessageAsync(IpcMessage msg)
    {
        try
        {
            await _writeGate.WaitAsync().ConfigureAwait(false);

            try
            {
                if (_cmdPipe != null && _cmdPipe.IsConnected)
                    await PipeRequestBinarySerializer.WriteMessageAsync(_cmdPipe, msg).ConfigureAwait(false);
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

                await _writeGate.WaitAsync(token).ConfigureAwait(false);

                try
                {
                    _eventPipe = eventPipe;
                    _cmdPipe = cmdPipe;
                }

                finally
                {
                    _writeGate.Release();
                }

                await Task.WhenAll(

                    eventPipe.ConnectAsync(5000, token),
                    cmdPipe.ConnectAsync(5000, token)

                ).ConfigureAwait(false);
                Logger.Log("[HookIpcClient] Connected to hook pipes.", LogLevel.Debug);

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
                    OnPathCaptured?.Invoke(msg.StringVal1 ?? string.Empty, msg.IsDesktop);
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
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _launchBroker.Dispose();
    }
}
