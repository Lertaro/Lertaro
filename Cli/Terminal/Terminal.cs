using System.Runtime.InteropServices;

namespace Lertaro.Cli.Terminal;

// Talks to the console device directly through CONOUT$/CONIN$ handles, never through Console.Out/
// Console.In/Console.ReadKey. Two independent reasons:
//
// Output: stdout is reserved entirely for the one thing meant to leave this process -- the selected
// path(s) printed on Enter -- so `lff | xargs code` or `$path = & lff.exe` works the same way piping
// fzf's own output does. If the UI painted to stdout instead, every repaint's raw ANSI bytes would land
// in that pipe/capture right alongside the real result. It also sidesteps a correctness issue, not just
// a cosmetic one: once stdout is redirected to a file/pipe, .NET's Console.WindowWidth/CursorTop/
// SetCursorPosition/CursorVisible all throw (they resolve through the STD_OUTPUT_HANDLE, which a
// redirected process no longer has pointed at an actual console) -- this CLI would crash immediately
// under `| something`, not just misbehave visually.
//
// Input: stdin can likewise be redirected to supply the CLI's initial query (`echo report | lff`), which
// makes Console.ReadKey throw ("console input has been redirected from a file") the moment the
// interactive loop tries to read a keystroke -- so key events are read off a directly-opened CONIN$
// handle too, via the raw ReadConsoleInputW API, completely independent of whatever the process's own
// stdin is doing.
public sealed class Terminal
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    private const uint ENABLE_LINE_INPUT = 0x0002;
    private const uint ENABLE_ECHO_INPUT = 0x0004;
    private const ushort KEY_EVENT = 1;
    private const uint SHIFT_PRESSED = 0x0010;
    private const uint ALT_PRESSED = 0x0001 | 0x0002; // LEFT_ALT_PRESSED | RIGHT_ALT_PRESSED
    private const uint CTRL_PRESSED = 0x0004 | 0x0008; // LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleScreenBufferInfo(IntPtr hConsoleOutput, out CONSOLE_SCREEN_BUFFER_INFO lpConsoleScreenBufferInfo);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadConsoleInputW(IntPtr hConsoleInput, [Out] INPUT_RECORD[] lpBuffer, uint nLength, out uint lpNumberOfEventsRead);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WriteConsoleW(IntPtr hConsoleOutput, string lpBuffer, uint nNumberOfCharsToWrite, out uint lpNumberOfCharsWritten, IntPtr lpReserved);

    private readonly IntPtr _conOutHandle;
    private readonly IntPtr _conInHandle;

    private Terminal(IntPtr conOutHandle, IntPtr conInHandle)
    {
        _conOutHandle = conOutHandle;
        _conInHandle = conInHandle;
    }

    // Returns null (having already written the specific reason to stderr) if either handle can't be
    // opened -- e.g. no real console at all, such as a fully detached/non-interactive host.
    public static Terminal? TryOpen()
    {
        var conOutHandle = CreateFileW("CONOUT$", GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (conOutHandle == IntPtr.Zero || conOutHandle == new IntPtr(-1))
        {
            Console.Error.WriteLine("[error] Could not open the console (CONOUT$) -- this tool needs an interactive terminal to run in, even when its final output is piped elsewhere.");
            return null;
        }
        if (GetConsoleMode(conOutHandle, out var conOutMode))
            SetConsoleMode(conOutHandle, conOutMode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);

        // Only line/echo input are turned off -- ReadConsoleInputW already returns raw per-keystroke
        // events regardless of them, and leaving ENABLE_ECHO_INPUT on would have the console itself print
        // each typed character in addition to Renderer drawing the input line, doubling it.
        // ENABLE_PROCESSED_INPUT is deliberately left ON: clearing it doesn't just stop the console from
        // echoing "^C" -- Ctrl+C stops being turned into a signal at all and instead arrives as an
        // ordinary keystroke event (ETX, 0x03), which nothing in the key-handling loop treats as "exit",
        // so Ctrl+C silently did nothing the one time this was cleared. Leaving it set restores the
        // normal OS behavior (Ctrl+C -> Console.CancelKeyPress), independent of which other keys
        // ReadConsoleInputW reports.
        var conInHandle = CreateFileW("CONIN$", GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (conInHandle == IntPtr.Zero || conInHandle == new IntPtr(-1))
        {
            Console.Error.WriteLine("[error] Could not open the console input (CONIN$) -- this tool needs an interactive terminal to run in.");
            return null;
        }
        if (GetConsoleMode(conInHandle, out var conInMode))
            SetConsoleMode(conInHandle, conInMode & ~(ENABLE_ECHO_INPUT | ENABLE_LINE_INPUT));

        return new Terminal(conOutHandle, conInHandle);
    }

    // Writes straight through WriteConsoleW rather than wrapping conOutHandle in a FileStream/
    // StreamWriter: a FileStream write goes through WriteFile with encoded bytes, which the console then
    // reinterprets according to whatever its ACTIVE OUTPUT CODE PAGE happens to be (not necessarily
    // UTF-8) -- a CJK filename's multi-byte UTF-8 sequence decoded under, say, GBK produces garbage, and
    // critically that garbage can swallow/shift adjacent bytes too, corrupting a still-to-come ANSI
    // escape sequence right along with it (which is exactly what once rendered "\x1b[39m" as the literal
    // text "?[39m" -- the escape byte survived, but landed mid-garbage and never got recognized as one).
    // WriteConsoleW takes UTF-16 text directly with no codepage translation at all, sidestepping the
    // whole problem.
    public void WriteConsole(string text)
    {
        if (text.Length > 0)
            WriteConsoleW(_conOutHandle, text, (uint)text.Length, out _, IntPtr.Zero);
    }

    public int GetConsoleWidth() => GetConsoleScreenBufferInfo(_conOutHandle, out var info) ? Math.Max(1, (int)info.srWindow.Right - info.srWindow.Left + 1) : 80;
    public int GetConsoleCursorRow() => GetConsoleScreenBufferInfo(_conOutHandle, out var info) ? info.dwCursorPosition.Y : 0;

    // Blocks until the next actual key-down event, skipping key-up events and non-keyboard input records
    // (mouse/window/focus) that ReadConsoleInputW can also surface. Returns false only if the handle
    // itself is gone (e.g. the console window was closed out from under this process).
    public bool TryReadKey(out ConsoleKeyInfo keyInfo)
    {
        var buffer = new INPUT_RECORD[1];
        while (true)
        {
            if (!ReadConsoleInputW(_conInHandle, buffer, 1, out var read) || read == 0)
            {
                keyInfo = default;
                return false;
            }

            var rec = buffer[0];
            if (rec.EventType != KEY_EVENT || !rec.KeyEvent.bKeyDown)
                continue;

            var state = rec.KeyEvent.dwControlKeyState;
            keyInfo = new ConsoleKeyInfo(
                rec.KeyEvent.UnicodeChar,
                (ConsoleKey)rec.KeyEvent.wVirtualKeyCode,
                (state & SHIFT_PRESSED) != 0,
                (state & ALT_PRESSED) != 0,
                (state & CTRL_PRESSED) != 0);
            return true;
        }
    }
}
