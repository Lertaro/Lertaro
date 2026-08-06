using System.Runtime.InteropServices;

namespace Lertaro.Cli.Terminal;

[StructLayout(LayoutKind.Sequential)]
internal struct COORD
{
    public short X;
    public short Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SMALL_RECT
{
    public short Left;
    public short Top;
    public short Right;
    public short Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CONSOLE_SCREEN_BUFFER_INFO
{
    public COORD dwSize;
    public COORD dwCursorPosition;
    public ushort wAttributes;
    public SMALL_RECT srWindow;
    public COORD dwMaximumWindowSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEY_EVENT_RECORD
{
    public bool bKeyDown;
    public ushort wRepeatCount;
    public ushort wVirtualKeyCode;
    public ushort wVirtualScanCode;
    public char UnicodeChar;
    public uint dwControlKeyState;
}

// Win32's INPUT_RECORD is `WORD EventType; union { KEY_EVENT_RECORD; MOUSE_EVENT_RECORD; ... } Event;` --
// KeyEvent is the only variant Terminal.TryReadKey reads (it skips anything whose EventType isn't
// KEY_EVENT), so the other union members (mouse/focus/window-buffer-size/menu events) aren't declared at
// all; the [FieldOffset(4)] below (past the 2-byte EventType field, 4-byte aligned) is what actually
// overlays KeyEvent onto the union's memory the way the other variants would occupy too.
[StructLayout(LayoutKind.Explicit)]
internal struct INPUT_RECORD
{
    [FieldOffset(0)] public ushort EventType;
    [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
}
