using System.Windows.Threading;
using Lertaro.Core;
using Lertaro.Core.Hook;
using Application = System.Windows.Application;
using Lertaro.App.ViewModels.Search;
using Lertaro.App.Views.InlineSearchWindow.Helpers;

using Lertaro.App.Services.ShellIcons;
using Lertaro.Core.Wire;
using Lertaro.Core.Hook.InlineSearch;
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
    private string _searchText = string.Empty;
    private IntPtr _currentHostHwnd = IntPtr.Zero;

    public ExplorerTracker ExplorerTracker => _explorerTracker;
    public KeyboardHookService KeyboardHook => _keyboardHook;
    public MouseHookService MouseHook => _mouseHook;
    public string SearchText => _searchText;

    private InlineSearchManager()
    {
        _explorerTracker = new ExplorerTracker();
        _keyboardHook = new KeyboardHookService(_explorerTracker);
        _mouseHook = new MouseHookService(IsPointInsideWindow);

        if (App.HookClient != null)
        {
            App.HookClient.OnExplorerActivated += (hwnd, title, className, isDesktop) => _explorerTracker.UpdateActiveWindow(hwnd, title, className, isDesktop);
            App.HookClient.OnExplorerDeactivated += () => _explorerTracker.DeactivateWindow();
            App.HookClient.OnPathCaptured += (path, isDesktop) => _explorerTracker.UpdatePath(path, isDesktop);
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

    private bool IsPointInsideWindow(int x, int y)
    {
        if (_window == null || !_window.IsVisible) return false;
        return _window.IsPointInsideWindowExternal(x, y);
    }

    private void WireUpExplorerEvents()
    {
        _explorerTracker.OnExplorerActivated += (hwnd, title, className, isDesktop) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_window != null && _currentHostHwnd == hwnd)
                {
                    return;
                }

                if (_explorerTracker.IsActiveWindowDialog)
                {
                    CloseInlineSearch("ExplorerActivated (Dialog)");
                    EnsureWindowCreated();
                    _window?.UpdateSearchDisplay(string.Empty);
                }
                else
                {
                    CloseInlineSearch("ExplorerActivated (Non-Dialog)");
                }
            }));

        _explorerTracker.OnExplorerDeactivated += () => Application.Current.Dispatcher.BeginInvoke(new Action(ScheduleCloseOnExplorerDeactivated));

        _explorerTracker.OnError += (msg) => Logger.Log($"[InlineSearchManager] ExplorerTracker error: {msg}", LogLevel.Error);

        _explorerTracker.OnPathCaptured += (path, isDesktop) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_window != null)
                {
                    var oldScope = _window.ViewModel.SearchScope;
                    if (oldScope != path)
                    {
                        _window.ViewModel.SearchScope = path;
                        Logger.Log($"[InlineSearchManager] Updated SearchScope dynamically to: {path}", LogLevel.Debug);

                        if (string.IsNullOrEmpty(_window.SearchText))
                            _window.ViewModel.Search.PerformSearch(string.Empty);
                    }
                }
                else if (_explorerTracker.IsActiveWindowDialog)
                {
                    EnsureWindowCreated();
                    _window?.UpdateSearchDisplay(string.Empty);
                }
            }));
    }

    private void WireUpMouseEvents() => _mouseHook.OnClickOutside += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                                                 {
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
                EnsureWindowCreated();
                _window?.UpdateSearchDisplay(_searchText);
            },
            onBackspacePressed: () =>
            {
                if (_searchText.Length > 0)
                {
                    _searchText = _searchText.Substring(0, _searchText.Length - 1);
                    EnsureWindowCreated();
                    _window?.UpdateSearchDisplay(_searchText);
                }
            });

        router.Wire();
    }

    private void EnsureWindowCreated()
    {
        // As early as possible, before Show() -- see PowerThrottlingHelper's own comment. Idempotent, so
        // it's harmless to call this even on the (common) already-created short-circuit below.
        PowerThrottlingHelper.WindowShowing("inline");

        if (_window != null) return;

        var viewModel = new QuickSearchViewModel();
        var scope = _explorerTracker.ActivePath;
        if (string.IsNullOrEmpty(scope) && _explorerTracker.ActiveHwnd != IntPtr.Zero)
        {
            // ActiveInlineAdapter is always null for a plain IFileDialogAdapter host (WinRAR's Extract
            // dialog, Explorer's classic/folder-browser dialogs, ...) -- only ActiveAdapter applies there.
            // Falling back to ActivePath's own poller cycle alone meant SearchScope stayed empty for
            // every window recreated between polls (the window gets torn down and rebuilt on every
            // SetInlineSearchVisible toggle), which in turn broke ExplorerJumpSuggestionHelper's own
            // "already scoped here, don't suggest jumping" check for the entire gap.
            if (_explorerTracker.ActiveInlineAdapter != null)
                scope = _explorerTracker.ActiveInlineAdapter.GetSearchScope(_explorerTracker.ActiveHwnd);
            else if (_explorerTracker.ActiveAdapter != null)
                scope = _explorerTracker.ActiveAdapter.GetCurrentPath(_explorerTracker.ActiveHwnd);
        }
        viewModel.SearchScope = scope;
        viewModel.IsInlineSearchContext = true;

        _window = new InlineSearchWindow(viewModel, this);
        _currentHostHwnd = _explorerTracker.ActiveHwnd;
        _keyboardHook.IsInlineSearchVisible = true;
        // Set alongside it here but, unlike it, not cleared when the window takes focus below: this one
        // tracks the window being on screen, which both of those paths leave true.
        _keyboardHook.IsInlineWindowOnScreen = true;
        _mouseHook.Start();

        // Force the native HWND into existence now (still invisible -- EnsureHandle doesn't set
        // WS_VISIBLE) rather than letting Show() create it implicitly. PositionWindowImmediate needs a
        // real PresentationSource to read the correct per-monitor DPI from; called any earlier, it falls
        // back to VisualTreeHelper.GetDpi on a windowless visual, which isn't reliably the DPI of
        // whatever monitor this window is actually about to land on (especially across monitors with
        // different scaling) -- computing the right logical position with the wrong DPI still produces a
        // visibly wrong physical-pixel position, just a different wrong one than leaving Left/Top
        // untouched entirely.
        new System.Windows.Interop.WindowInteropHelper(_window).EnsureHandle();
        _window.Positioner.PositionWindowImmediate();
        _window.Show();
        _window.ViewModel.EnsureServiceMonitoringActive();

        var fgHwnd = ExplorerNativeHooks.GetForegroundWindow();
        var isTextInputFocused = fgHwnd != IntPtr.Zero && InputFocusEvaluator.IsForegroundTextInputFocused(fgHwnd);

        if (!isTextInputFocused && !_explorerTracker.IsActiveWindowDialog)
        {
            // Try to activate and focus synchronously first while we are still in the input/hook processing thread context
            if (_window.ActivateAndFocusSearchBox())
            {
                _keyboardHook.IsInlineSearchVisible = false;
                _keyboardHook.Stop();
            }
            else
            {
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_window == null || !_window.IsVisible)
                    {
                        return;
                    }

                    if (_window.ActivateAndFocusSearchBox())
                    {
                        _keyboardHook.IsInlineSearchVisible = false;
                        _keyboardHook.Stop();
                    }
                }), DispatcherPriority.Input);
            }
        }
        else
        {
            // If a text input is already focused, show the window without stealing focus,
            // and restore focus to the edit box.
            var dialogHwnd = _explorerTracker.ActiveHwnd;
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (dialogHwnd != IntPtr.Zero)
                {
                    ExplorerNativeHooks.SetForegroundWindow(dialogHwnd);
                    var editBox = ExplorerNativeHooks.FindSubEditBox(dialogHwnd);
                    if (editBox != IntPtr.Zero)
                        ExplorerNativeHooks.SetFocus(editBox);
                }
            }), DispatcherPriority.Input);
        }

        Logger.Log($"[InlineSearchManager] Created and shown new InlineSearchWindow. Scope: {viewModel.SearchScope}", LogLevel.Debug);
    }

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
        if (_window == null) return;

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

        _mouseHook.Stop();
        _keyboardHook.IsInlineSearchVisible = false;
        _keyboardHook.IsInlineWindowOnScreen = false;
        _keyboardHook.Start();
        _searchText = string.Empty;

        var win = _window;
        _window = null;
        _currentHostHwnd = IntPtr.Zero;
        win.ViewModel.Monitor.StopStatusTimer();
        win.Hide();
        win.Close();
        PowerThrottlingHelper.WindowHidden("inline");

        // Inline search closes whenever you leave Explorer; release the icon cache and trim the working
        // set each time, matching QuickSearch's hide behavior, so inline-only users reclaim memory too.
        ShellIconHelper.ClearCache();
        PathCacheMaintenance.ClearAllPathCaches();
        Win32Api.TrimWorkingSet();

        Logger.Log($"[InlineSearchManager] InlineSearchWindow closed and destroyed. Reason: {reason}", LogLevel.Debug);
    }

    public bool IsInlineSearchActive => _window != null && _window.IsVisible;

    public void FocusSearchBox()
    {
        if (_window != null && _window.IsVisible)
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
        _keyboardHook.Dispose();
        _mouseHook.Dispose();
        _explorerTracker.Dispose();
    }
}
