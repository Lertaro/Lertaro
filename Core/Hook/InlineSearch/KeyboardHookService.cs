using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Core.Hook.InlineSearch;

// The raw key-code-to-inline-search-event translation logic lives in
// KeyboardHookServiceInlineSearchExtensions.cs (extension methods, matching TreeBuilder's own
// Checkpoint/Diff extension split) instead of a partial class, to keep this file under the project's
// line limit. That extension needs access to this service's settings/tracker/context-menu-grace state,
// so those fields are `internal` rather than `private`.
public class KeyboardHookService : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private KeyboardNativeMethods.LowLevelKeyboardProc? _proc;
    internal readonly ExplorerTracker _explorerTracker;

    internal UserSettings _settings = UserSettings.Load();
    internal GlobalHotkeyDetector _hotkeyDetector;

    internal bool _hasPendingContextMenuTrigger;
    internal uint _lastContextMenuTriggerTime;
    internal const uint ContextMenuGraceMs = 400;

    public event Action? OnDoubleCtrl;
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

    // Trampolines letting KeyboardHookServiceInlineSearchExtensions raise these events on this
    // instance's behalf -- C# event accessors can only be invoked from the declaring class itself, even
    // for an `internal` event, so a caller outside it needs one of these. Matches ExplorerTracker's own
    // RaiseExplorerActivated/RaisePathCaptured/RaiseError trampolines.
    internal void RaiseCharacterTyped(char ch) => OnCharacterTyped?.Invoke(ch);
    internal void RaiseBackspacePressed() => OnBackspacePressed?.Invoke();
    internal void RaiseEscapePressed() => OnEscapePressed?.Invoke();
    internal void RaiseEnterPressed() => OnEnterPressed?.Invoke();
    internal void RaiseUpPressed() => OnUpPressed?.Invoke();
    internal void RaiseDownPressed() => OnDownPressed?.Invoke();
    internal void RaiseLeftPressed() => OnLeftPressed?.Invoke();
    internal void RaiseRightPressed() => OnRightPressed?.Invoke();
    internal void RaiseCtrlNumberPressed(int num) => OnCtrlNumberPressed?.Invoke(num);

    public bool IsQuickSearchWindowVisible { get; set; }
    public bool IsInlineSearchVisible { get; set; }

    // "The inline window is on screen", which stays true after IsInlineSearchVisible is cleared by the
    // window taking focus for itself. Suppressions that must last as long as the window is up use this.
    public bool IsInlineWindowOnScreen { get; set; }
    public uint AppProcessId { get; set; }
    public bool IsHotkeysDisabledTemporarily { get; set; }

    public KeyboardHookService(ExplorerTracker explorerTracker)
    {
        _explorerTracker = explorerTracker;
        _hotkeyDetector = new GlobalHotkeyDetector(_settings, _explorerTracker);
    }
    public void ReloadSettings()
    {
        _settings = UserSettings.ForceReload();
        _hotkeyDetector = new GlobalHotkeyDetector(_settings, _explorerTracker);
        Logger.Log("[KeyboardHookService] Hotkey settings reloaded.", LogLevel.Info);
    }
    // Wired from MouseHookService.OnRightButtonDown, and also called directly below for the VK_APPS
    // (Menu) key. Windows needs real, non-zero time to build a context menu (shell extension
    // IContextMenu handlers, etc.), so either trigger immediately followed by a menu mnemonic keypress
    // can beat GUI_INMENUMODE becoming true -- this records the trigger so HandleInlineSearchKeys can
    // give it a short grace window regardless.
    public void NotifyRightButtonDown(uint time) => MarkPendingContextMenuTrigger(time);

    private void MarkPendingContextMenuTrigger(uint time)
    {
        _hasPendingContextMenuTrigger = true;
        _lastContextMenuTriggerTime = time;
    }
    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;
        _proc = HookCallback;
        var hMod = KeyboardNativeMethods.GetModuleHandle(null);
        _hookId = KeyboardNativeMethods.SetWindowsHookEx(KeyboardNativeMethods.WH_KEYBOARD_LL, _proc, hMod, 0);
        if (_hookId == IntPtr.Zero)
        {
            Logger.Log($"[KeyboardHookService] Failed to install keyboard hook! Error={Marshal.GetLastWin32Error()}", LogLevel.Error);
        }
    }
    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            KeyboardNativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Catch-all around the whole callback: an exception escaping a native low-level hook callback
        // terminates the process (taking every global hook down with it). The body includes plugin
        // reclassification and other fallible work -- log and let the keystroke pass through instead.
        try
        {
            return HookCallbackCore(nCode, wParam, lParam);
        }
        catch (Exception ex)
        {
            Logger.Log($"[KeyboardHookService] Hook callback error: {ex.Message}", LogLevel.Error);
            return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }

    private IntPtr HookCallbackCore(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)KeyboardNativeMethods.WM_KEYUP || wParam == (IntPtr)KeyboardNativeMethods.WM_SYSKEYUP))
        {
            var hookStruct = Marshal.PtrToStructure<KeyboardNativeMethods.KBDLLHOOKSTRUCT>(lParam);
            _hotkeyDetector.OnKeyUp((int)hookStruct.vkCode);
        }
        if (nCode >= 0 && (wParam == (IntPtr)KeyboardNativeMethods.WM_KEYDOWN || wParam == (IntPtr)KeyboardNativeMethods.WM_SYSKEYDOWN))
        {
            var hookStruct = Marshal.PtrToStructure<KeyboardNativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var vkCode = (int)hookStruct.vkCode;
            var time = hookStruct.time;
            _hotkeyDetector.OnKeyDown(vkCode);

            // The physical Menu/context-menu key, and Shift+F10, both open a context menu just like a
            // right-click does, with the same real, non-zero construction delay -- covered
            // unconditionally here (not gated by shouldDisableAllHooks below), matching how the
            // independent mouse hook's own right-click detection is never gated either. F10 arrives as
            // WM_SYSKEYDOWN, which this outer condition already includes alongside WM_KEYDOWN.
            // GetAsyncKeyState, not GetKeyState: the LL hook owner's thread has no per-thread key
            // state of its own (same reasoning as the LLKHF_ALTDOWN comment in KeyboardNativeMethods),
            // so only the async, process-global state answers usefully here.
            var isShiftF10 = vkCode == KeyboardNativeMethods.VK_F10
                && (KeyboardNativeMethods.GetAsyncKeyState(KeyboardNativeMethods.VK_SHIFT) & 0x8000) != 0;
            if (vkCode == KeyboardNativeMethods.VK_APPS || isShiftF10)
            {
                MarkPendingContextMenuTrigger(time);
            }

            // Alt+Space and Alt+F4 while the inline window is up, giving it the same two suppressions
            // SystemMenuBlocker gives the quick window. It carries that blocker itself, but the block
            // only takes when it holds the foreground: if a text input was already focused,
            // InlineSearchManager shows it without stealing focus and puts the foreground back on the
            // host dialog, so the keys go to Explorer (or whatever dialog it is attached to), which
            // opens its own system menu over the search box or closes itself out from under it. The
            // window's own hook never sees those messages, which is why they have to be caught here.
            //
            // Swallowing keys aimed at another process is worth it only for these two combinations and
            // only while our own UI is on screen: with the search box showing and taking every other
            // keystroke, both are a misfire rather than an intent.
            //
            // Gated on IsInlineWindowOnScreen, not IsInlineSearchVisible: that one means "forward
            // keystrokes to me" and is cleared the moment the window takes focus for itself, which is
            // the very case this has to cover.
            //
            // Alt comes from the event's own flags, not GetKeyState: that reports the calling thread's
            // key state, and this callback runs on the hook owner's thread rather than the one the
            // keystroke was headed for, so it reports nothing useful here.
            var isAltDown = (hookStruct.flags & KeyboardNativeMethods.LLKHF_ALTDOWN) != 0;
            if (IsInlineWindowOnScreen && isAltDown
                && (vkCode == KeyboardNativeMethods.VK_SPACE || vkCode == KeyboardNativeMethods.VK_F4))
            {
                return (IntPtr)1;
            }

            // 1. Detect Toggle Window Hotkey
            // Fullscreen apps share the same gate as the process blacklist: it only suppresses the
            // quick-window toggle, Quick Switch, and inline-search invocation below, and still yields
            // to an active file dialog (IsActiveWindowDialog) same as the blacklist does. Key-up
            // tracking and Explorer-tracker bookkeeping run unconditionally either way.
            var isFullscreenBlocking = !_settings.Hotkeys.AllowHotkeysInFullscreen && FullscreenHelper.IsForegroundWindowFullScreen();
            var shouldDisableAllHooks = (IsHotkeysDisabledTemporarily || ForegroundProcessGate.IsForegroundProcessBlacklisted(_settings.BlacklistedProcesses) || isFullscreenBlocking)
                                         && !_explorerTracker.IsActiveWindowDialog;

            // The quick panel first: a plain combination with no bare-modifier form, so nothing below
            // is waiting to see whether this key turns out to be part of a tap.
            if (!shouldDisableAllHooks && _hotkeyDetector.CheckQuickPanelHotkey(vkCode, out var consumeQuickPanel))
            {
                OnQuickPanelHotkey?.Invoke();
                if (consumeQuickPanel) return (IntPtr)1;
            }

            if (!shouldDisableAllHooks && _hotkeyDetector.CheckToggleWindowHotkey(vkCode, time, out var consumeToggleKey, OnDoubleCtrl))
            {
                if (consumeToggleKey)
                {
                    return (IntPtr)1;
                }
            }
            // 2. Filter key inputs for Inline Search
            if (IsQuickSearchWindowVisible)
            {
                return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            var fgHwnd = KeyboardNativeMethods.GetForegroundWindow();
            if (fgHwnd != IntPtr.Zero)
            {
                KeyboardNativeMethods.GetWindowThreadProcessId(fgHwnd, out var fgPid);
                if (fgPid == AppProcessId || fgPid == (uint)Environment.ProcessId)
                {
                    var sbClass = new StringBuilder(256);
                    ExplorerNativeHooks.GetClassName(fgHwnd, sbClass, sbClass.Capacity);
                    if (!sbClass.ToString().Equals("#32770", StringComparison.OrdinalIgnoreCase))
                    {
                        return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }
                }
                var rootFg = ExplorerNativeHooks.GetAncestor(fgHwnd, ExplorerNativeHooks.GA_ROOTOWNER);
                if (rootFg == IntPtr.Zero) rootFg = fgHwnd;
                if (_explorerTracker.ActiveHwnd != IntPtr.Zero && rootFg != _explorerTracker.ActiveHwnd)
                {
                    if (!IsDescendantOrOwned(_explorerTracker.ActiveHwnd, fgHwnd) && !IsImeWindow(fgHwnd))
                    {
                        // Re-derive state for whatever's actually foreground now (fgHwnd), not just wipe
                        // to "nothing is active" -- the tracker's own async WinEvent-based tracking can
                        // lag behind real foreground changes (e.g. a dialog re-gaining focus right after
                        // Explorer briefly lost it to nothing), and this runs synchronously on the very
                        // same keystroke Quick Switch (Ctrl+G) reads IsActiveWindowDialog from a few
                        // lines below -- blindly deactivating here could clear that flag to false right
                        // before Quick Switch's own check saw it, on the keystroke that was supposed to
                        // trigger it in the first place.
                        // Bounded variant: this runs inside the LL keyboard hook callback, where any
                        // stall past LowLevelHooksTimeout gets the hook silently dropped -- see
                        // ReclassifyActiveWindowBounded.
                        _explorerTracker.ReclassifyActiveWindowBounded(fgHwnd);
                    }
                }
            }
            // 3. Detect and handle Quick Switch Hotkey
            if (!shouldDisableAllHooks && _hotkeyDetector.CheckAndHandleQuickSwitch(vkCode, time, out var consumeQuickSwitchKey))
            {
                if (consumeQuickSwitchKey)
                {
                    return (IntPtr)1;
                }
            }
            // If text input is focused, bypass
            if (fgHwnd != IntPtr.Zero && InputFocusEvaluator.IsForegroundTextInputFocused(fgHwnd))
            {
                return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            if (_explorerTracker.IsActiveWindowDialog)
            {
                return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            // 4. Handle Inline Search key events
            if (!shouldDisableAllHooks && this.HandleInlineSearchKeys(vkCode, hookStruct, fgHwnd))
            {
                return (IntPtr)1;
            }
        }
        return KeyboardNativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
    public void Dispose() => Stop();

    private bool IsDescendantOrOwned(IntPtr parent, IntPtr child)
    {
        if (parent == IntPtr.Zero || child == IntPtr.Zero) return false;
        if (parent == child) return true;

        var current = child;
        while (current != IntPtr.Zero)
        {
            if (current == parent) return true;
            var temp = ExplorerNativeHooks.GetParent(current);
            if (temp == IntPtr.Zero || temp == current) break;
            current = temp;
        }
        var rootOwner = ExplorerNativeHooks.GetAncestor(child, ExplorerNativeHooks.GA_ROOTOWNER);
        if (rootOwner == parent) return true;

        return false;
    }
    private bool IsImeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        var sbClass = new StringBuilder(256);
        ExplorerNativeHooks.GetClassName(hwnd, sbClass, sbClass.Capacity);
        var fgClass = sbClass.ToString();
        return fgClass.Contains("IME", StringComparison.OrdinalIgnoreCase) ||
               fgClass.Contains("Candidate", StringComparison.OrdinalIgnoreCase) ||
               fgClass.Contains("InputTip", StringComparison.OrdinalIgnoreCase) ||
               fgClass.Contains("InputSwitch", StringComparison.OrdinalIgnoreCase);
    }
}
