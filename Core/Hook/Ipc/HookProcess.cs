using System.Runtime.InteropServices;
using Lertaro.Core.Hook.InlineSearch;

using Lertaro.Core.Wire;
using Lertaro.Core.Hook.Commands;
using Lertaro.PluginSdk.Services;
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
    // Stop() can legitimately arrive before RunMessageLoop has even entered (the App's stop request
    // races hook-process startup). Record it separately: RunMessageLoop used to overwrite the stop
    // request by setting _running = true unconditionally, and the WM_QUIT posted before the native
    // thread id existed was a no-op -- leaving the main loop blocked in GetMessage forever with every
    // hook still installed.
    private volatile bool _stopRequested;
    private uint _appProcessId;
    private int _watchedAppPid;
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

    /// <summary>
    /// The App-side half of <see cref="ToolRunService.RunDopusPathsFunc"/>: the App runs
    /// the tool at the user's own privilege level, and this reads what it wrote.
    /// </summary>
    /// <remarks>
    /// The file is created here first because the tool only fills in a file that already exists, and it is
    /// written by the App with its own token, so the content has to come back through the file rather than
    /// through the pipe.
    /// </remarks>
    private static async Task<string?> RunToolViaAppAsync(string toolPath, string outputFile)
    {
        try
        {
            try { File.Delete(outputFile); } catch { /* it may not exist yet */ }

            var ran = await HookToolRunRequest.RunDopusRtAsync(toolPath, outputFile, TimeSpan.FromSeconds(4)).ConfigureAwait(false);
            if (!ran) return null;

            // The tool writes before exiting, so this is a short grace period rather than the main wait.
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    var file = new FileInfo(outputFile);
                    if (file.Exists && file.Length > 0)
                    {
                        var text = File.ReadAllText(outputFile);
                        if (!string.IsNullOrWhiteSpace(text)) return text;
                    }
                }
                catch (IOException) { /* still being written, or briefly locked: try again */ }
                catch (UnauthorizedAccessException) { /* same */ }

                await Task.Delay(25).ConfigureAwait(false);
            }

            Logger.Log($"[DirectoryOpus] the App ran dopusrt but wrote nothing to '{outputFile}'.", LogLevel.Debug);
            return null;
        }
        catch (Exception ex)
        {
            Logger.Log($"[DirectoryOpus] running dopusrt through the App failed: {ex.Message}", LogLevel.Debug);
            return null;
        }
        finally
        {
            try { File.Delete(outputFile); } catch { /* our own temp file, best effort */ }
        }
    }

    internal uint AppProcessId
    {
        get => _appProcessId;
        set
        {
            _appProcessId = value;
            WatchAppLiveness(value);
        }
    }

    /// <summary>
    /// Whether the hook should shut itself down because the App it was launched for is gone: the watched
    /// pid must still be the App the hook believes it serves (the App can restart and register a new one,
    /// and a stale watch on the old pid must not stop the hook under the new one), and a pid of 0 means no
    /// App ever registered -- which is the state a hook started by the service sits in before the App
    /// connects, and must not self-stop.
    /// </summary>
    internal static bool ShouldSelfStop(uint watchedPid, uint currentAppPid, bool appIsGone) =>
        watchedPid != 0 && watchedPid == currentAppPid && appIsGone;

    /// <summary>
    /// Nothing else watched the App. The only other shutdown signal is a Stop *message*, which a crashed
    /// App by definition never sends, and the App's own Kill() of this process died with it -- so a crash
    /// left an elevated hook with system-wide keyboard and mouse hooks installed for the rest of the
    /// logon session, still swallowing the keys its inline-window flags gate.
    /// </summary>
    private void WatchAppLiveness(uint pid)
    {
        if (pid == 0 || Volatile.Read(ref _watchedAppPid) == (int)pid)
            return;

        Volatile.Write(ref _watchedAppPid, (int)pid);
        _ = Task.Run(async () =>
        {
            var gone = true;
            try
            {
                using var app = System.Diagnostics.Process.GetProcessById((int)pid);
                await app.WaitForExitAsync().ConfigureAwait(false);
            }
            catch (ArgumentException)
            {
                // The pid is already gone by the time the watch starts -- the same answer as exiting.
            }
            catch (Exception ex)
            {
                // Cannot tell whether the App is alive: leave the hook running, which is what happened
                // unconditionally before, rather than stopping a working hook on a lookup fault.
                Logger.Log($"[HookProcess] App liveness watch on pid {pid} failed: {ex.Message}", LogLevel.Warn);
                gone = false;
            }

            if (!ShouldSelfStop(pid, _appProcessId, gone))
                return;

            Logger.Log($"[HookProcess] App pid {pid} is gone; stopping the hook instead of outliving it.", LogLevel.Info);
            Stop();
        });
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

        // Lets a plugin ask the App to run something at the App's own (unelevated) privilege level --
        // see HookToolRunRequest for why the Hook cannot do that itself.
        HookToolRunRequest.SendToApp = _ipcServer.SendMessage;
        ToolRunService.RunDopusPathsFunc = RunToolViaAppAsync;
        // See ExplorerTracker.PublishCurrentState's own comment: Start()'s one-time startup activation
        // check almost always loses the race against the App actually connecting over the pipe, and
        // that lost snapshot was the App's only chance to learn the true initial state otherwise.
        _ipcServer.OnConnected += () =>
        {
            _explorerTracker?.PublishCurrentState();
            // Off the accept loop, via the same path the command handler uses. OnConnected is raised
            // before the writer pump and the command reader exist, and building this snapshot queries
            // file managers by launching a process that can hang for seconds -- a collector that
            // round-trips through this pipe cannot get its reply from a pump that is not running yet, so
            // doing it inline spent its full timeout here while the hook answered nothing at all.
            _commandHandler.PublishOpenedFoldersOffThread();
        };
        _ipcServer.OnDisconnected += () =>
        {
            // Whatever the window state was, the window it described is no longer answering: these flags
            // are what the hook uses to decide which keystrokes to swallow, and only the App clears them.
            // A crash with the inline window open used to leave the hook suppressing Escape and the arrows
            // in every application on the desktop.
            if (_keyboardHook == null)
                return;

            _keyboardHook.IsInlineSearchVisible = false;
            _keyboardHook.IsInlineWindowOnScreen = false;
            _keyboardHook.IsQuickSearchWindowVisible = false;
            Logger.Log("[HookProcess] App link dropped; inline and quick-window suppression flags cleared.", LogLevel.Debug);
        };
    }

    public void RunMessageLoop()
    {
        _nativeThreadId = GetCurrentThreadId();
        // Honor a Stop() that arrived before this point: the WM_QUIT it posted was a no-op, so the
        // flag is the only reliable signal. With _running false the tracker thread and the message
        // loop below exit immediately and the finally block cleans the freshly installed hooks up.
        _running = !_stopRequested;

        // Not disposed at the end of the block: on a timeout this method moves on while the tracker
        // thread may still be about to call Set(), and disposing first would turn that into an exception
        // escaping a background thread. ManualResetEventSlim only allocates its kernel handle if
        // WaitHandle is touched, which nothing here does.
        var trackerStartedEvent = new ManualResetEventSlim(false);
        var trackerStartedInTime = false;
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
                    _explorerTracker.OnPathCaptured += (path, isDesktop, isDialog) => _ipcServer.SendMessage(new IpcMessage
                    {
                        Id = IpcMessageId.PathCaptured,
                        StringVal1 = path,
                        IsDesktop = isDesktop,
                        IsDialog = isDialog
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

            // Bounded on purpose. ExplorerTracker.Start() ends with an active-window classification whose
            // plugin probes are not all routed through the bounded STA invoker, so a third-party plugin
            // that blocks in one of them never reaches the Set() below -- and an unbounded Wait parked
            // this thread before any hook was installed or any pipe served, which the App answered by
            // killing and relaunching the hook every 5 s with no backoff, so inline search never came up.
            // A tracker that will not start within the window now gets the same abort as a null one, which
            // was always the intended answer and simply could not be reached on time.
            trackerStartedInTime = trackerStartedEvent.Wait(TimeSpan.FromSeconds(10));
            if (!trackerStartedInTime)
            {
                _running = false; // let the tracker thread's own loop fall out instead of pumping messages
                Logger.Log("[HookProcess] The Explorer tracker did not start within 10s; aborting.", LogLevel.Error);
            }
        }

        if (_explorerTracker == null || !trackerStartedInTime)
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
            _keyboardHook.OnQuickNavigationHotkey += () => _ipcServer.SendQuickNavigationHotkey();
            _keyboardHook.OnDoubleCtrl += () =>
            {
                Logger.Log("[HookProcess] Double-Ctrl detected, sending ACTIVATE.", LogLevel.Debug);
                // Docked inline search handles this activation by focusing its existing search bar. It
                // must not be mistaken for the separate quick window, or the hook will pass through all
                // following keys while the inline window is still on screen.
                var inlineWindowIsActive = _keyboardHook.IsInlineSearchVisible || _keyboardHook.IsInlineWindowOnScreen;
                _keyboardHook.IsQuickSearchWindowVisible = !inlineWindowIsActive;
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
            _keyboardHook.OnFocusInlineSearchRequested += () => _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.FocusInlineSearch });
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

    // A temporarily-disabled, blacklisted, or fullscreen foreground app suppresses quick-nav mouse
    // triggers, except recognized file dialogs and file managers which remain eligible.
    // Reads the hook service's cached settings rather than UserSettings.Load(): this runs inside the
    // mouse hook callback, where a settings file I/O error (rethrown after persistence retries) would
    // escape a native hook callback and terminate the process. ReloadSettings keeps the cache current.
    private bool ShouldSuppressQuickNavTrigger() => QuickNavigationHotkeyGate.ShouldSuppress(
        _explorerTracker?.IsActiveWindowDialog == true || _explorerTracker?.ActiveInlineAdapter?.IsFileExplorer == true,
        _isHotkeysDisabledTemporarily,
        ForegroundProcessGate.IsForegroundProcessBlacklisted(_keyboardHook?._settings.BlacklistedProcesses ?? []),
        FullscreenHelper.IsForegroundWindowFullScreen());

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
        _stopRequested = true;
        _running = false;
        if (_nativeThreadId != 0) PostThreadMessage(_nativeThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        if (_trackerThreadId != 0) PostThreadMessage(_trackerThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose() => Stop();
}
