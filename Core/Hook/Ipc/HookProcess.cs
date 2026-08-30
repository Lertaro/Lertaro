using System.Runtime.InteropServices;
using Lertaro.Core.Hook.InlineSearch;

using Lertaro.Core.Wire;
using Lertaro.Core.Hook.Commands;
namespace Lertaro.Core.Hook.Ipc;

public sealed class HookProcess : IDisposable
{
    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(int idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);



    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX; public int ptY; }

    private const uint WM_QUIT = 0x0012;
    private const uint WM_REFRESH_ACTIVE_ADAPTERS = 0x8001;

    private readonly HookIpcServer _ipcServer;
    private readonly HookCommandHandler _commandHandler;
    private readonly OpenedFolderSnapshotPublisher _openedFolderSnapshots;
    private ExplorerTracker? _explorerTracker;
    private KeyboardHookService? _keyboardHook;
    private MouseHookService? _mouseHook;

    private int _nativeThreadId;
    private int _trackerThreadId;
    private Thread? _trackerThread;
    private volatile bool _running;
    private uint _appProcessId;
    private bool _isHotkeysDisabledTemporarily;

    internal KeyboardHookService? KeyboardHook => _keyboardHook;
    internal ExplorerTracker? ExplorerTracker => _explorerTracker;
    internal HookIpcServer IpcServer => _ipcServer;

    internal void RefreshActiveWindowAdapters()
    {
        if (_trackerThreadId != 0)
            PostThreadMessage(_trackerThreadId, WM_REFRESH_ACTIVE_ADAPTERS, IntPtr.Zero, IntPtr.Zero);
    }

    internal void PublishOpenedFolders() => _openedFolderSnapshots.Publish();

    internal uint AppProcessId
    {
        get => _appProcessId;
        set => _appProcessId = value;
    }

    internal bool IsHotkeysDisabledTemporarily
    {
        get => _isHotkeysDisabledTemporarily;
        set => _isHotkeysDisabledTemporarily = value;
    }

    public HookProcess(HookIpcServer ipcServer)
    {
        _ipcServer = ipcServer;
        _commandHandler = new HookCommandHandler(this);
        _openedFolderSnapshots = new OpenedFolderSnapshotPublisher(_ipcServer);

        _ipcServer.OnStopRequested += () => Stop();
        _ipcServer.OnCommandReceived += _commandHandler.HandleAppCommand;
        // See ExplorerTracker.PublishCurrentState's own comment: Start()'s one-time startup activation
        // check almost always loses the race against the App actually connecting over the pipe, and
        // that lost snapshot was the App's only chance to learn the true initial state otherwise.
        _ipcServer.OnConnected += () =>
        {
            _explorerTracker?.PublishCurrentState();
            _openedFolderSnapshots.Publish();
        };
    }

    public void RunMessageLoop()
    {
        _nativeThreadId = GetCurrentThreadId();
        _running = true;

        using (var trackerStartedEvent = new ManualResetEventSlim(false))
        {
            _trackerThread = new Thread(() =>
            {
                _trackerThreadId = GetCurrentThreadId();
                try
                {
                    _explorerTracker = new ExplorerTracker();
                    _explorerTracker.AppProcessId = _appProcessId;
                    _explorerTracker.OnExplorerActivated += (hwnd, title, className, isDesktop) => _ipcServer.SendMessage(new IpcMessage
                    {
                        Id = IpcMessageId.ExplorerActivated,
                        Hwnd = hwnd.ToInt64(),
                        StringVal1 = title,
                        StringVal2 = className,
                        IsDesktop = isDesktop
                    });
                    _explorerTracker.OnExplorerDeactivated += () =>
                    {
                        _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.ExplorerDeactivated });
                        Task.Run(() =>
                        {
                            try { Win32Api.TrimWorkingSet(); } catch { }
                        });
                    };
                    _explorerTracker.OnPathCaptured += (path, isDesktop) => _ipcServer.SendMessage(new IpcMessage
                    {
                        Id = IpcMessageId.PathCaptured,
                        StringVal1 = path,
                        IsDesktop = isDesktop
                    });
                    _explorerTracker.OnActiveWindowMoved += () => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.ActiveWindowMoved });
                    _explorerTracker.OnError += (msg) => _ipcServer.SendMessage(new IpcMessage
                    {
                        Id = IpcMessageId.Error,
                        StringVal1 = msg
                    });
                    _explorerTracker.Start();
                    trackerStartedEvent.Set();

                    while (_running)
                    {
                        var result = GetMessage(out var msg, IntPtr.Zero, 0, 0);
                        if (result <= 0) break;
                        if (msg.message == WM_REFRESH_ACTIVE_ADAPTERS)
                        {
                            _explorerTracker?.RefreshActiveWindowAdapters();
                            continue;
                        }
                        TranslateMessage(ref msg);
                        DispatchMessage(ref msg);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[HookProcess] TrackerThread error: {ex.Message}", LogLevel.Error);
                    trackerStartedEvent.Set();
                }
            });
            _trackerThread.SetApartmentState(ApartmentState.STA);
            _trackerThread.IsBackground = true;
            _trackerThread.Start();

            trackerStartedEvent.Wait();
        }

        if (_explorerTracker == null)
        {
            Logger.Log("[HookProcess] Explorer tracker failed to start; aborting hook installation.", LogLevel.Error);
            CleanupHooks();
            return;
        }

        try
        {
            _keyboardHook = new KeyboardHookService(_explorerTracker);
            _keyboardHook.AppProcessId = _appProcessId;
            _keyboardHook.IsHotkeysDisabledTemporarily = _isHotkeysDisabledTemporarily;
            _keyboardHook.OnQuickPanelHotkey += () =>
            {
                Logger.Log("[HookProcess] Quick panel hotkey detected.", LogLevel.Debug);
                _ipcServer.SendQuickPanelHotkey();
            };
            _keyboardHook.OnDoubleCtrl += () =>
            {
                Logger.Log("[HookProcess] Double-Ctrl detected, sending ACTIVATE.", LogLevel.Debug);
                // Set this here, synchronously, in the same hook callback that detected the toggle --
                // don't wait for the App to show its window and round-trip an IPC confirmation back.
                // That round trip (App.ShowWindow -> IPC -> HookCommandHandler -> here) can still be
                // in flight when the user's very next keystroke arrives at HookCallback; until this
                // flag is true, HookCallback keeps routing keys through the inline-search path instead
                // of just letting them through for whatever window is about to own focus (see #121).
                _keyboardHook.IsQuickSearchWindowVisible = true;
                if (_appProcessId != 0)
                {
                    AllowSetForegroundWindow((int)_appProcessId);
                }
                _ipcServer.SendActivate();
            };
            _keyboardHook.OnCharacterTyped += ch => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyChar, CharVal = ch });
            _keyboardHook.OnBackspacePressed += () => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyBackspace });
            _keyboardHook.OnEscapePressed += () => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyEscape });
            _keyboardHook.OnEnterPressed += () => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyEnter });
            _keyboardHook.OnUpPressed += () => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyUp });
            _keyboardHook.OnDownPressed += () => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyDown });
            _keyboardHook.OnLeftPressed += () => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyLeft });
            _keyboardHook.OnRightPressed += () => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyRight });
            _keyboardHook.OnCtrlNumberPressed += num => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.KeyCtrlNumber, IntVal = num });
            _keyboardHook.Start();

            _mouseHook = new MouseHookService();
            _mouseHook.OnRightButtonDown += time => _keyboardHook.NotifyRightButtonDown(time);
            _mouseHook.OnMouseClick += (x, y) => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.MouseClick, MouseX = x, MouseY = y });
            _mouseHook.OnMouseDoubleClick += (x, y) =>
            {
                if (ShouldSuppressQuickNavTrigger()) return;
                Logger.Log($"[HookProcess] OnMouseDoubleClick at ({x}, {y}). ActiveHwnd={_explorerTracker?.ActiveHwnd}, IsExplorerOrDesktopActive={_explorerTracker?.IsExplorerOrDesktopActive}", LogLevel.Debug);
                _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.MouseDoubleClick, MouseX = x, MouseY = y });
            };
            _mouseHook.OnMouseMiddleClick += (x, y) =>
            {
                if (ShouldSuppressQuickNavTrigger()) return;
                Logger.Log($"[HookProcess] OnMouseMiddleClick at ({x}, {y}). ActiveHwnd={_explorerTracker?.ActiveHwnd}, IsExplorerOrDesktopActive={_explorerTracker?.IsExplorerOrDesktopActive}", LogLevel.Debug);
                _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.MouseMiddleClick, MouseX = x, MouseY = y });
            };
            _mouseHook.Start();

            Logger.Log("[HookProcess] Hooks and ExplorerTracker initialized successfully.", LogLevel.Info);

            while (_running)
            {
                var result = GetMessage(out var msg, IntPtr.Zero, 0, 0);
                if (result <= 0) break;
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        finally
        {
            CleanupHooks();
        }
    }

    // Same gate as KeyboardHookService's shouldDisableAllHooks: a temporarily-disabled, blacklisted, or
    // fullscreen foreground app suppresses the quick-nav mouse triggers too, but still yields to an
    // active file dialog so FileDialogQuickNavGate's middle-click-in-dialogs support keeps working.
    private bool ShouldSuppressQuickNavTrigger() => (_isHotkeysDisabledTemporarily
                || ForegroundProcessGate.IsForegroundProcessBlacklisted(UserSettings.Load().BlacklistedProcesses)
                || FullscreenHelper.IsForegroundWindowFullScreen())
               && !(_explorerTracker?.IsActiveWindowDialog ?? false);

    private void CleanupHooks()
    {
        _keyboardHook?.Dispose(); _keyboardHook = null;
        _mouseHook?.Dispose(); _mouseHook = null;
        if (_trackerThreadId != 0)
        {
            PostThreadMessage(_trackerThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        var trackerJoined = _trackerThread?.Join(2000) ?? true;
        _trackerThread = null;
        if (trackerJoined)
        {
            _explorerTracker?.Dispose();
            _explorerTracker = null;
            Logger.Log("[HookProcess] Hooks and ExplorerTracker stopped/cleaned up.", LogLevel.Info);
        }
        else
        {
            Logger.Log("[HookProcess] Tracker thread did not stop in time; skipping tracker dispose to avoid a race.", LogLevel.Warn);
        }
        try { Win32Api.TrimWorkingSet(); } catch { }
    }

    public void Stop()
    {
        _running = false;
        if (_nativeThreadId != 0) PostThreadMessage(_nativeThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        if (_trackerThreadId != 0) PostThreadMessage(_trackerThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose() => Stop();
}
