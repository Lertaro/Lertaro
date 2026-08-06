using Lertaro.Core.Wire;
using Lertaro.Core.Hook.Ipc;
namespace Lertaro.Core.Hook.Commands;

public sealed class HookCommandHandler
{
    private readonly HookProcess _process;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool fAttach);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

    private const byte VK_MENU = 0x12;
    private const byte VK_UNASSIGNED = 0xFF;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public HookCommandHandler(HookProcess process) => _process = process;

    public void HandleAppCommand(IpcMessage msg)
    {
        try
        {
            switch (msg.Id)
            {
                case IpcMessageId.SetQuickSearchVisible:
                    _process.KeyboardHook?.IsQuickSearchWindowVisible = msg.BoolVal;
                    break;
                case IpcMessageId.SetInlineSearchVisible:
                    _process.KeyboardHook?.IsInlineSearchVisible = msg.BoolVal;
                    break;
                case IpcMessageId.SetInlineWindowOnScreen:
                    _process.KeyboardHook?.IsInlineWindowOnScreen = msg.BoolVal;
                    break;
                case IpcMessageId.SetAppProcessId:
                    _process.AppProcessId = msg.ProcessId;
                    _process.KeyboardHook?.AppProcessId = msg.ProcessId;
                    _process.ExplorerTracker?.AppProcessId = msg.ProcessId;
                    break;
                case IpcMessageId.NavigateDialog:
                    FileDialogCommandHandler.HandleNavigateDialog(_process, msg);
                    break;
                case IpcMessageId.RestoreDialogFocus:
                    FileDialogCommandHandler.HandleRestoreDialogFocus(_process, msg);
                    break;

                case IpcMessageId.ForceForeground:
                    {
                        var appHwnd = (IntPtr)msg.Hwnd;
                        if (appHwnd != IntPtr.Zero)
                        {
                            Logger.Log($"[HookCommandHandler] Forcing foreground for HWND 0x{appHwnd.ToInt64():X}", LogLevel.Debug);

                            if (msg.BoolVal)
                            {
                                // Simulate Alt key press to bypass SetForegroundWindow restrictions. Only
                                // requested by callers whose invocation isn't already backed by very
                                // recent real user input on this thread (e.g. QuickSearchWindow's global
                                // hotkey activation) -- Quick Navigation's own ForceForeground call skips
                                // this (see QuickNavigationMenu.Show()): it fires right after the Hook's
                                // own mouse hook processed the click that triggered it, which already
                                // satisfies the foreground-lock check on its own, and this Alt tap was
                                // found to cause its own, self-inflicted deactivation of the popup shortly
                                // after (WPF's own keyboard input handling reacts to a bare Alt press/
                                // release independently of the SC_KEYMENU workaround below).
                                //
                                // keybd_event is async (system input queue) while SetForegroundWindow below
                                // takes effect immediately, so this Alt tap is often delivered to the app
                                // window AFTER it becomes the focus window. A lone Alt down+up makes
                                // DefWindowProc enter the invisible system-menu keyboard loop (SC_KEYMENU),
                                // which silently eats the next keystroke as a menu mnemonic -- the caret
                                // keeps blinking but the first typed character never arrives. Sandwich a
                                // reserved, unassigned VK (0xFF: no character, ignored by apps) inside the
                                // tap so the Alt counts as "used" and never activates menu mode, wherever
                                // the events end up landing.
                                keybd_event(VK_MENU, 0, 0, IntPtr.Zero);
                                keybd_event(VK_UNASSIGNED, 0, 0, IntPtr.Zero);
                                keybd_event(VK_UNASSIGNED, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
                                keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
                            }

                            var fgHwnd = GetForegroundWindow();
                            var fgThreadId = GetWindowThreadProcessId(fgHwnd, out _);
                            var currentThreadId = GetCurrentThreadId();

                            var attached = false;
                            if (fgThreadId != 0 && fgThreadId != currentThreadId)
                            {
                                attached = AttachThreadInput(currentThreadId, fgThreadId, true);
                            }

                            try
                            {
                                SetForegroundWindow(appHwnd);
                                SetActiveWindow(appHwnd);
                                SetFocus(appHwnd);
                            }
                            finally
                            {
                                if (attached)
                                {
                                    AttachThreadInput(currentThreadId, fgThreadId, false);
                                }
                            }
                        }
                    }
                    break;

                case IpcMessageId.ReloadSettings:
                    {
                        var newSettings = UserSettings.ForceReload();
                        if (Enum.TryParse<LogLevel>(newSettings.LogLevel, ignoreCase: true, out var newLogLevel))
                            Logger.MinimumLevel = newLogLevel;
                        _process.KeyboardHook?.ReloadSettings();
                    }
                    break;
                case IpcMessageId.SetHotkeysDisabled:
                    _process.IsHotkeysDisabledTemporarily = msg.BoolVal;
                    _process.KeyboardHook?.IsHotkeysDisabledTemporarily = msg.BoolVal;
                    break;
                case IpcMessageId.ExecuteInlineItem:
                case IpcMessageId.InlineSelectionChanged:
                case IpcMessageId.InlineSearchFinished:
                    InlineAdapterCommandHandler.Handle(_process, msg);
                    break;
                case IpcMessageId.KillProcess:
                    {
                        var pid = (int)msg.ProcessId;
                        if (pid != 0)
                        {
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                try
                                {
                                    // Kill only the target process, not its descendant tree: a process
                                    // can legitimately have unrelated child processes (e.g. a helper
                                    // tool launched through it), and entireProcessTree:true would take
                                    // those down too even though the user only asked to end this one.
                                    using var proc = System.Diagnostics.Process.GetProcessById(pid);
                                    proc.Kill();
                                    Logger.Log($"[HookCommandHandler] Killed process {pid} successfully", LogLevel.Info);
                                }
                                catch (Exception ex)
                                {
                                    Logger.Log($"[HookCommandHandler] Failed to kill process {pid}: {ex.Message}", LogLevel.Warn);
                                }
                            });
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[HookCommandHandler] Error parsing IPC command {msg.Id}: {ex.Message}", LogLevel.Warn);
        }
    }
}
