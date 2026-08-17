namespace Lertaro.Core.Wire;

public enum IpcMessageId : byte
{
    // App -> Hook
    Stop = 1,
    SetAppProcessId = 2,
    SetQuickSearchVisible = 3,
    SetInlineSearchVisible = 4,
    NavigateDialog = 5,
    RestoreDialogFocus = 6,
    ReloadSettings = 7,
    SetHotkeysDisabled = 8,
    ForceForeground = 13,
    KillProcess = 14,
    ExecuteInlineItem = 15,
    InlineSelectionChanged = 16,
    InlineSearchFinished = 17,
    // Distinct from SetInlineSearchVisible, which means "forward keystrokes to me" and is cleared the
    // moment the inline window takes focus for itself. This one means the window is simply on screen,
    // which stays true either way, and is what a suppression that has to outlive that handover needs.
    SetInlineWindowOnScreen = 18,
    RequestOpenedFolders = 19,

    // Hook -> App
    Activate = 20,
    ExplorerDeactivated = 21,
    ActiveWindowMoved = 22,
    KeyBackspace = 23,
    KeyEscape = 24,
    KeyEnter = 25,
    KeyUp = 26,
    KeyDown = 27,
    KeyLeft = 28,
    KeyRight = 29,
    KeyChar = 30,
    KeyCtrlNumber = 31,
    MouseClick = 32,
    ExplorerActivated = 33,
    PathCaptured = 34,
    Error = 35,
    MouseDoubleClick = 38,
    MouseMiddleClick = 39,
    // Hook -> App: the quick panel's global hotkey fired. Carries nothing; the panel reads the
    // foreground window itself, which has to be the one in front at that moment rather than whatever
    // the hook happened to see.
    QuickPanelHotkey = 36,
    ExecuteInlineItemResponse = 40,
    OpenedFoldersCaptured = 41
}

public struct IpcMessage
{
    public IpcMessageId Id { get; set; }
    public uint ProcessId { get; set; }
    public bool BoolVal { get; set; }
    public char CharVal { get; set; }
    public int IntVal { get; set; }
    public int MouseX { get; set; }
    public int MouseY { get; set; }
    public long Hwnd { get; set; }
    public string? StringVal1 { get; set; }
    public string? StringVal2 { get; set; }
    public IReadOnlyList<string>? StringList { get; set; }
    public bool IsDesktop { get; set; }
}
