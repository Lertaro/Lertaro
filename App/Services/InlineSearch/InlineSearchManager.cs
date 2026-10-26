using System.Windows.Threading;
using Lertaro.Core;
using Lertaro.Core.Hook;
using Application = System.Windows.Application;
using Lertaro.App.Views.InlineSearchWindow.Helpers;
using Lertaro.App.Helpers.Visuals;

using Lertaro.App.Services.ShellIcons;
using Lertaro.Core.Wire;
namespace Lertaro.App.Services;

/// <summary>
/// Manages the lifecycle of InlineSearchWindow and keeps hooks persistent
/// so the window can be created and destroyed dynamically on user input.
/// </summary>
public class InlineSearchManager : IDisposable
{
    private static InlineSearchManager? _instance;
    public static InlineSearchManager Instance => _instance ??= new InlineSearchManager();

    private InlineSearchWindow? _window;
    private readonly ExplorerTracker _explorerTracker;
    private readonly KeyboardHookService _keyboardHook;
    private readonly MouseHookService _mouseHook;
    private readonly InlineSearchWindowSummonSupport _windowSummon;
    private string _searchText = string.Empty;
    private IntPtr _currentHostHwnd = IntPtr.Zero;
    // The card never watches the folder itself: a live FileSystemWatcher holds a directory handle on the
    // volume, and a removable volume carrying one cannot be ejected until this process exits -- the card
    // parks for the whole session, so its handle would too. The index reports the same changes from the
    // process that owns the volume handle and releases it on a PnP query-remove.
    private const string FolderWatchKey = "__inline-search";
    private IDisposable? _folderWatch;
    private string? _watchedFolder;

    public ExplorerTracker ExplorerTracker => _explorerTracker;
    public KeyboardHookService KeyboardHook => _keyboardHook;
    public MouseHookService MouseHook => _mouseHook;
    public string SearchText => _searchText;
    internal InlineSearchWindow? Window { get => _window; set => _window = value; }
    internal IntPtr CurrentHostHwnd { get => _currentHostHwnd; set => _currentHostHwnd = value; }

    private InlineSearchManager()
    {
        _explorerTracker = new ExplorerTracker();
        _keyboardHook = new KeyboardHookService(_explorerTracker);
        _mouseHook = new MouseHookService(IsPointInsideWindow);
        _windowSummon = new InlineSearchWindowSummonSupport(this);

        if (App.HookClient != null)
        {
            App.HookClient.OnExplorerActivated += (hwnd, title, className, isDesktop) => _explorerTracker.UpdateActiveWindow(hwnd, title, className, isDesktop);
            App.HookClient.OnExplorerDeactivated += () => _explorerTracker.DeactivateWindow();
            App.HookClient.OnPathCaptured += (path, isDesktop, isDialog) => _explorerTracker.UpdatePath(path, isDesktop, isDialog);
            App.HookClient.OnActiveWindowMoved += () => _explorerTracker.MoveActiveWindow();
            App.HookClient.OnError += msg => _explorerTracker.RaiseErrorExternal(msg);
        }

        WireUpExplorerEvents();
        WireUpMouseEvents();
        WireUpKeyboardEvents();
    }

    public void Start()
    {
        _keyboardHook.Start();
        Logger.Log("[InlineSearchManager] Services started.", LogLevel.Debug);
    }

    // MouseHookService evaluates this on the hook's IPC thread, where reading a WPF property throws --
    // so only the Win32 point test runs here and the window's own visibility is decided in
    // WireUpMouseEvents, back on the UI thread.
    private bool IsPointInsideWindow(int x, int y) => _window?.IsPointInsideWindowExternal(x, y) ?? false;

    private void WireUpExplorerEvents()
    {
        _explorerTracker.OnExplorerActivated += (hwnd, title, className, isDesktop) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsInlineSearchActive && _currentHostHwnd == hwnd)
                {
                    return;
                }

                if (_explorerTracker.IsActiveWindowDialog)
                {
                    CloseInlineSearch("ExplorerActivated (Dialog)");
                    SummonWindow();
                    // The window exists and knows its folder, but the user has not typed yet -- start
                    // reading that folder now so the first character does not have to wait for the walk.
                    PrewarmActiveFolderListing();
                }
                else
                {
                    CloseInlineSearch("ExplorerActivated (Non-Dialog)");
                }
            }));

        _explorerTracker.OnExplorerDeactivated += () => Application.Current.Dispatcher.BeginInvoke(new Action(ScheduleCloseOnExplorerDeactivated));

        _explorerTracker.OnError += (msg) => Logger.Log($"[InlineSearchManager] ExplorerTracker error: {msg}", LogLevel.Error);

        _explorerTracker.OnPathCaptured += (path, _, _) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_window is { IsVisible: true })
                {
                    var oldScope = _window.ViewModel.SearchScope;
                    if (oldScope != path)
                    {
                        _window.ViewModel.SearchScope = path;
                        Logger.Log($"[InlineSearchManager] Updated SearchScope dynamically to: {path}", LogLevel.Debug);

                        // Re-run with the text as it stands, not only when the box is empty. The scope just
                        // replaced is the one the CURRENT results were computed against, so leaving them on
                        // screen shows the previous folder's rows -- measured as "the first character does not
                        // locate the folder, the second one does", because that second keystroke was the only
                        // thing that re-ran the search with the repaired scope. No extra waiting and no extra
                        // work when the path did not change: this is the same call the empty-box case made.
                        // The box itself is asked rather than the tracked string: they agree once a keystroke
                        // has been handled, but while an IME is composing the box is already ahead of it, and
                        // re-running with the older text would compute the delayed scope against a query the
                        // user has moved on from.
                        _window.ViewModel.Search.PerformSearch(_window.SearchBox.SearchText ?? string.Empty);
                    }
                }
                else if (_explorerTracker.IsActiveWindowDialog)
                {
                    SummonWindow();
                }

                // Covers both branches above: a scope just changed, or a card just summoned for a dialog
                // host. Either way the folder is known before anything is typed.
                PrewarmActiveFolderListing();
            }));
    }

    // Starts reading the inline window's current folder so its first keystroke matches a warm listing
    // instead of walking the folder (see DirectChildrenListingCache). Best-effort: with no window there is
    // nothing to warm, and the empty-query search that also runs here does no listing work of its own.
    private void PrewarmActiveFolderListing()
    {
        var scope = _window?.ViewModel.SearchScope;
        if (string.IsNullOrEmpty(scope))
            return;
        _window!.ViewModel.Search.PrewarmDirectoryListing(scope);
        WatchFolder(scope);
    }

    // The folder listing is otherwise fixed for the window's life: delete a file in Explorer and every
    // later keystroke re-filtered the same stale entries, so the row stayed on screen (see
    // DirectChildrenListingCache). The index's change stream turns a folder change into an invalidation
    // plus a re-run of whatever the box currently says.
    //
    // Deliberately not a FileSystemWatcher (see FolderWatchKey): the index is the source of truth anyway,
    // and its notification is raised after the batch was applied, so the re-run cannot stream back a row
    // the change already removed -- which is what the old watcher's extra settle pass existed for.
    private void WatchFolder(string folder)
    {
        if (string.Equals(_watchedFolder, folder, StringComparison.OrdinalIgnoreCase))
            return;

        _ = Core.Services.Plugin.DirectoryIndex.CoreDirectoryIndexManager.Instance;

        _folderWatch ??= PluginSdk.Services.DirectoryIndexerService.WatchDirectories(FolderWatchKey, OnWatchedFolderChanged);
        // One key holds one folder: dropping the previous registration is what stops the old folder's
        // changes from still refreshing this card after Explorer moved elsewhere.
        if (_watchedFolder != null)
            PluginSdk.Services.DirectoryIndexerService.UnregisterDirectories(FolderWatchKey);

        _watchedFolder = folder;
        // One level, because the listing carries this folder's own entries. The change matcher reports
        // descendants as well, which can only cost one extra re-run.
        PluginSdk.Services.DirectoryIndexerService.RegisterDirectory(FolderWatchKey, folder, recursive: false, "*");
    }

    // Raised on a background thread, already debounced and already after the index caught up; the card
    // only has to get back to its own thread.
    private void OnWatchedFolderChanged() =>
        Application.Current?.Dispatcher.BeginInvoke(new Action(ApplyFolderChange));

    private void ApplyFolderChange()
    {
        var scope = _window?.ViewModel.SearchScope;
        if (string.IsNullOrEmpty(scope))
            return;
        _window?.ViewModel.Search.InvalidateDirectoryListing(scope);
        // A hidden window keeps the invalidation only: the next summon runs a query anyway, and re-running
        // for an invisible card would walk a folder nobody is looking at.
        if (_window is not { IsVisible: true })
            return;

        _window!.ViewModel.Search.PerformSearch(_window.SearchBox.SearchText ?? string.Empty);
    }

    private void DisposeFolderWatcher()
    {
        _folderWatch?.Dispose();
        _folderWatch = null;
        if (_watchedFolder != null)
            PluginSdk.Services.DirectoryIndexerService.UnregisterDirectories(FolderWatchKey);
        _watchedFolder = null;
    }

    private void WireUpMouseEvents() => _mouseHook.OnClickOutside += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                                                 {
                                                     // Reads IsVisible here rather than in the predicate:
                                                     // the hook delivers this on its IPC thread, and the
                                                     // InvalidOperationException it threw there used to kill
                                                     // the click-outside dismissal for every click.
                                                     if (_window == null || !_window.IsVisible) return;
                                                     if (_explorerTracker.IsActiveWindowDialog)
                                                         return;
                                                     CloseInlineSearch("ClickOutside");
                                                 }));

    private void WireUpKeyboardEvents()
    {
        var router = new InlineSearchKeyboardEventRouter(
            _keyboardHook,
            getWindow: () => _window,
            onCharacterTyped: ch =>
            {
                if (ch != '\0')
                {
                    _searchText += ch;
                }
                SummonWindow();
                _window?.UpdateSearchDisplay(_searchText);
            },
            onBackspacePressed: () =>
            {
                if (_searchText.Length > 0)
                {
                    _searchText = _searchText.Substring(0, _searchText.Length - 1);
                    SummonWindow();
                    _window?.UpdateSearchDisplay(_searchText);
                }
            });

        router.Wire();
    }

    private void SummonWindow() => _windowSummon.Summon();

    public bool IsExecuting { get; set; }

    // A transient foreground steal fires ExplorerDeactivated and would instantly close the inline window
    // mid-typing -- e.g. reading a \\wsl$ result's icon/date on a background thread wakes the WSL VM, whose
    // cold start briefly flashes a conhost that grabs the foreground. Wait a moment and only close if the
    // foreground really left both Explorer and this window (i.e. it didn't just bounce back).
    private void ScheduleCloseOnExplorerDeactivated()
    {
        if (_window == null) return;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            if (_window == null) return;
            if (_explorerTracker.IsExplorerOrDesktopActive) return; // focus bounced back to Explorer/Desktop
            var fg = InlineSearchWindowNativeMethods.GetForegroundWindow();
            var self = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
            if (fg == self) return; // focus bounced back to the inline window itself
            CloseInlineSearch("ExplorerDeactivated");
        };
        timer.Start();
    }

    public void CloseInlineSearch(string reason = "Unknown") => CloseInlineSearch(reason, null);

    // deferUntil: null on the initial (outermost) call -- set to a real deadline the first time this
    // has to defer, so the recursive BeginInvoke retries below share one bounded wait instead of each
    // starting a fresh 500ms clock (which could stall the close indefinitely as long as SOMETHING
    // keeps looking "active").
    private void CloseInlineSearch(string reason, DateTime? deferUntil)
    {
        if (_window == null)
        {
            // Win32 destroys the card together with the dialog that owned it (see
            // InlineSearchWindowSummonSupport.Create), which nulls the cached window without ever reaching the
            // reset below. The hook flags must not outlive the card: a stale IsInlineSearchVisible keeps the
            // hook swallowing Enter/Backspace/IME keys in windows whose text input it does not recognize.
            ResetHookState();
            return;
        }

        var dragActive = Views.Controls.Results.ResultsDragDropHelper.IsDragActive;
        var pendingMouseDown = _window.HasPendingMouseDown;

        if (dragActive || pendingMouseDown)
        {
            // Several callers of CloseInlineSearch (the "click outside" mouse hook in particular)
            // arrive asynchronously via Dispatcher.BeginInvoke -- and DoDragDrop's own nested OLE
            // message loop pumps this app's dispatcher queue too, so this can run REENTRANTLY while a
            // drag from the results list is still in flight, or while a left-button press elsewhere in
            // this window hasn't been matched by a release yet. Destroying this window's HWND (Hide()+
            // Close() below) in either case leaves WPF's mouse button state or the OS drag cursor stuck
            // (see ResultsDragDropHelper.IsDragActive's and InlineSearchWindow.HasPendingMouseDown's own
            // comments). Retry until whichever condition triggered this resolves naturally -- bounded to
            // 500ms so a press whose release genuinely never reaches this app (it went to some other
            // window entirely) doesn't leave the inline window permanently stuck open instead.
            var deadline = deferUntil ?? DateTime.UtcNow.AddMilliseconds(500);
            if (DateTime.UtcNow < deadline)
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => CloseInlineSearch(reason, deadline)), DispatcherPriority.Background);
                return;
            }
        }

        if (_explorerTracker.ActiveInlineAdapter != null && _explorerTracker.ActiveHwnd != IntPtr.Zero)
        {
            App.HookClient?.SendMessage(new IpcMessage
            {
                Id = IpcMessageId.InlineSearchFinished,
                Hwnd = _explorerTracker.ActiveHwnd.ToInt64(),
                BoolVal = IsExecuting
            });
        }
        IsExecuting = false;

        ResetHookState();

        var win = _window;
        _currentHostHwnd = IntPtr.Zero;
        win.ViewModel.Monitor.StopStatusTimer();
        // Parked, not destroyed. A card built for every summon brings its view model, its visual tree and its
        // first composition with it, and the user watches all of that (170-258ms of an on-screen window with
        // nothing painted in it, scratch/blackprobe); dismissed, it also loses the folder listing it warmed.
        // The parking coordinates are what keep window enumerators from reporting a hidden window's last
        // rectangle -- see WindowParking.
        win.RestoreKeyboardLayout();
        win.Hide();
        WindowParking.Park(win);
        // After the park, so the card is off its host before anything else can destroy the host and take the
        // card with it: a parked card is meant to survive and be re-docked by the next summon.
        win.ReleaseHost();
        PowerThrottlingHelper.WindowHidden("inline");

        // Inline search closes whenever you leave Explorer, so release the icon cache and let the working
        // set be handed back -- but through the same IDLE path the quick window already uses, not a trim
        // here. Trimming eagerly forces two blocking full GCs on the Enter-to-open path, and its own gate
        // documents the deeper cost: evicted pages must fault back in on the next summon, which measured
        // as most of that summon's time (see IdleWorkingSetTrimGate). WindowHidden arms the trim for once
        // the process actually goes quiet, and a summon cancels it.
        ShellIconHelper.ClearCache();
        PathCacheMaintenance.ClearAllPathCaches();
        IdleWorkingSetTrimmer.WindowHidden();

        Logger.Log($"[InlineSearchManager] InlineSearchWindow dismissed and parked. Reason: {reason}", LogLevel.Debug);
    }

    // Idempotent: the only state that outlives the card lives in the hook process, so it is also reachable
    // when the card was destroyed underneath us and there is nothing left to hide or park.
    private void ResetHookState()
    {
        _mouseHook.Stop();
        _keyboardHook.IsInlineSearchVisible = false;
        _keyboardHook.IsInlineWindowOnScreen = false;
        _keyboardHook.Start();
        _searchText = string.Empty;
    }

    public bool IsInlineSearchActive => _window is { IsVisible: true };

    public void FocusSearchBox()
    {
        if (_window is { IsVisible: true })
        {
            if (_explorerTracker.IsActiveWindowDialog
                && _window.SearchBox.SearchTextBox.IsKeyboardFocusWithin
                && string.IsNullOrEmpty(_window.SearchText))
            {
                _window.ResetInlineSearchAndFocusDialog();
                return;
            }
            _window.ActivateAndFocusSearchBox();
        }
    }

    public void Dispose()
    {
        CloseInlineSearch("Dispose");
        DisposeFolderWatcher();
        // The only place the card is destroyed: it outlives every summon now, and closing it is what releases
        // its view model's search-service session and its watchdog timer.
        _window?.Close();
        _window = null;
        _keyboardHook.Dispose();
        _mouseHook.Dispose();
        _explorerTracker.Dispose();
    }
}
