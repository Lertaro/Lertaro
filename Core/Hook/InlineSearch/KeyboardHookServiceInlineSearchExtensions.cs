using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Core.Hook.InlineSearch;

// Raw key-code-to-inline-search-event translation, split out of KeyboardHookService.cs (extension
// methods, matching TreeBuilder's own Checkpoint/Diff extension split) to keep that file under the
// project's line limit. Needs broad access to the hook service's settings/tracker/context-menu-grace
// state, which is why those fields are `internal` on KeyboardHookService rather than `private`.
internal static class KeyboardHookServiceInlineSearchExtensions
{
    internal static bool HandleInlineSearchKeys(this KeyboardHookService service, int vkCode, KeyboardNativeMethods.KBDLLHOOKSTRUCT hookStruct, IntPtr fgHwnd)
    {
        // A synthesized key event (SendInput/keybd_event) from some other process -- e.g. a
        // third-party automation tool's own virtual-key hotkey scheme (reported: Quicker's Right-Ctrl
        // + number combo) -- was otherwise indistinguishable from the user's own typing, so it got
        // swallowed as inline-search input (or as a "jump to result N" shortcut, if it happened to
        // match SelectJumpModifier) instead of reaching whatever it was actually meant for.
        if ((hookStruct.flags & KeyboardNativeMethods.LLKHF_INJECTED) != 0)
        {
            return false;
        }

        var targetFocus = fgHwnd;
        var threadId = KeyboardNativeMethods.GetWindowThreadProcessId(fgHwnd, out var fgPid);
        var guiInfo = new KeyboardNativeMethods.GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<KeyboardNativeMethods.GUITHREADINFO>()
        };
        var hasGuiInfo = KeyboardNativeMethods.GetGUIThreadInfo(threadId, ref guiInfo);

        // A context/system menu (right-click menu, title-bar menu, or a submenu of either) is
        // currently open. Explorer doesn't move keyboard focus to the menu HWND while it's up --
        // guiInfo.hwndFocus below still resolves to whatever control opened it -- so without this,
        // a menu mnemonic/accelerator keypress (e.g. "r" for Properties) got swallowed as the first
        // inline-search character instead of reaching the menu.
        const uint menuModeFlags = KeyboardNativeMethods.GUI_INMENUMODE
            | KeyboardNativeMethods.GUI_SYSTEMMENUMODE
            | KeyboardNativeMethods.GUI_POPUPMENUMODE;
        if (hasGuiInfo && (guiInfo.flags & menuModeFlags) != 0)
        {
            return false;
        }

        // The menu isn't confirmed open yet, but a right-click or Menu-key press landed very recently --
        // most likely the menu it opens just hasn't finished being built. Give it a short grace window
        // before treating a fast trigger-then-mnemonic as inline-search input. Compared using the raw
        // hook timestamps (both are the same GetTickCount-based clock) rather than wall-clock "now", so
        // our own processing latency never inflates or shrinks the measured gap.
        if (service._hasPendingContextMenuTrigger)
        {
            var elapsedSinceTrigger = unchecked((int)hookStruct.time - (int)service._lastContextMenuTriggerTime);
            if (elapsedSinceTrigger >= 0 && elapsedSinceTrigger <= KeyboardHookService.ContextMenuGraceMs)
            {
                return false;
            }
            service._hasPendingContextMenuTrigger = false;
        }

        if (hasGuiInfo && guiInfo.hwndFocus != IntPtr.Zero)
        {
            targetFocus = guiInfo.hwndFocus;
        }
        var sbClass = new StringBuilder(256);
        KeyboardNativeMethods.GetClassName(targetFocus, sbClass, sbClass.Capacity);
        var className = sbClass.ToString();

        var processName = ForegroundProcessGate.GetProcessNameWithoutExtension(fgPid);

        // Guarded rather than left to Logger.Log's own level check: this runs for every key pressed outside
        // a recognised text box, and string.Format would build the message even with Debug logging off.
        if (Logger.IsEnabled(LogLevel.Debug))
            Logger.Log(string.Format("[KeyboardHookService] HandleInlineSearchKeys: targetFocus=0x{0:X}, className={1}, processName={2}", targetFocus.ToInt64(), className, processName), LogLevel.Debug);

        if (service._explorerTracker.ActiveInlineAdapter == null)
        {
            var matched = PluginSdk.Registries.InlineSearchAdapterRegistry.GetMatchingAdapter(targetFocus, className, processName);
            if (matched != null)
            {
                service._explorerTracker.SetActiveInlineAdapterDirectly(matched, targetFocus);
            }
        }
        var isAdapterActive = service._explorerTracker.ActiveInlineAdapter != null;
        if (Logger.IsEnabled(LogLevel.Debug))
            Logger.Log(string.Format("[KeyboardHookService] HandleInlineSearchKeys: isAdapterActive={0}, ActiveInlineAdapter={1}", isAdapterActive, service._explorerTracker.ActiveInlineAdapter?.GetType().Name ?? "null"), LogLevel.Debug);
        if (service.IsInlineSearchVisible || isAdapterActive)
        {
            if (!service.IsInlineSearchVisible && isAdapterActive)
            {
                var canTrigger = service._explorerTracker.ActiveInlineAdapter!.CanTrigger(targetFocus, className);
                if (Logger.IsEnabled(LogLevel.Debug))
                    Logger.Log(string.Format("[KeyboardHookService] HandleInlineSearchKeys: CanTrigger={0}", canTrigger), LogLevel.Debug);
                if (!canTrigger)
                {
                    return false;
                }
            }
            return service.HandleInlineSearchTriggerKey(vkCode, hookStruct, fgHwnd);
        }
        return false;
    }

    private static bool HandleInlineSearchTriggerKey(this KeyboardHookService service, int vkCode, KeyboardNativeMethods.KBDLLHOOKSTRUCT hookStruct, IntPtr fgHwnd)
    {
        var isIndexModifierDown = !string.IsNullOrEmpty(service._settings.Hotkeys.SelectJumpModifier)
            && KeyboardUtils.CheckModifiersMatchOnly(service._settings.Hotkeys.SelectJumpModifier, service._hotkeyDetector.IsWindowsKeyDown);
        if (isIndexModifierDown && service.IsInlineSearchVisible)
        {
            var num = -1;
            if (vkCode >= 0x31 && vkCode <= 0x39)
                num = vkCode - 0x31 + 1;
            else if (vkCode >= 0x61 && vkCode <= 0x69)
                num = vkCode - 0x61 + 1;

            if (num >= 1 && num <= 9)
            {
                service.RaiseCtrlNumberPressed(num);
                return true; // Consume
            }
        }
        var ctrlDown = (KeyboardNativeMethods.GetKeyState(0x11) & 0x8000) != 0;
        var altDown = (KeyboardNativeMethods.GetKeyState(0x12) & 0x8000) != 0;
        var winDown = (KeyboardNativeMethods.GetKeyState(0x5B) & 0x8000) != 0 ||
                       (KeyboardNativeMethods.GetKeyState(0x5C) & 0x8000) != 0;
        if (ctrlDown || altDown || winDown)
        {
            return false;
        }
        if (vkCode == KeyboardNativeMethods.VK_ESCAPE)
        {
            if (service.IsInlineSearchVisible)
            {
                service.RaiseEscapePressed();
                return true;
            }
            return false;
        }
        if (vkCode == KeyboardNativeMethods.VK_BACK && service.IsInlineSearchVisible)
        {
            service.RaiseBackspacePressed();
            return true;
        }
        if (vkCode == KeyboardNativeMethods.VK_RETURN && service.IsInlineSearchVisible)
        {
            service.RaiseEnterPressed();
            return true;
        }
        if (vkCode == KeyboardNativeMethods.VK_UP && service.IsInlineSearchVisible)
        {
            service.RaiseUpPressed();
            return true;
        }
        if (vkCode == KeyboardNativeMethods.VK_DOWN && service.IsInlineSearchVisible)
        {
            service.RaiseDownPressed();
            return true;
        }
        if (vkCode == KeyboardNativeMethods.VK_LEFT && service.IsInlineSearchVisible)
        {
            service.RaiseLeftPressed();
            return true;
        }
        if (vkCode == KeyboardNativeMethods.VK_RIGHT && service.IsInlineSearchVisible)
        {
            service.RaiseRightPressed();
            return true;
        }
        if (vkCode == KeyboardNativeMethods.VK_TAB)
        {
            return false;
        }
        var isTriggerKey = (vkCode == KeyboardNativeMethods.VK_PROCESSKEY) ||
                            (vkCode >= 0x41 && vkCode <= 0x5A) ||
                            (vkCode >= 0x30 && vkCode <= 0x39) ||
                            (vkCode >= 0x60 && vkCode <= 0x69);

        if (isTriggerKey)
        {
            // When an IME is composing, ignore what/how many keys are pressed: just pop the (empty)
            // inline window and keep swallowing keys until focus is taken. Never let them through to
            // the host window (which would drive the system's default IME composition popup instead).
            var imeOn = vkCode == KeyboardNativeMethods.VK_PROCESSKEY || KeyboardUtils.IsImeActive(fgHwnd);
            if (imeOn)
            {
                if (!service.IsInlineSearchVisible)
                {
                    service.RaiseCharacterTyped('\0');
                }
                return true;
            }

            if (!service.IsInlineSearchVisible)
            {
                // No IME: inject the first typed character as before; later keys go to the focused box.
                var ch = KeyboardUtils.GetUnicodeChar(hookStruct);
                service.RaiseCharacterTyped(ch);
                return true;
            }
            return false;
        }
        return false;
    }
}
