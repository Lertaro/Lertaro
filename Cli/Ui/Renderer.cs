using System.Text;
using Lertaro.Cli.Search;

namespace Lertaro.Cli.Ui;

// Draws SearchSession's current state (query text, result rows, status line) onto a Terminal. Knows
// nothing about search/debouncing/pipes -- it only reads state and writes ANSI-escaped text.
public sealed class Renderer
{
    private readonly Terminal.Terminal _terminal;
    private readonly SearchSession _session;
    private readonly int _originTop;

    public Renderer(Terminal.Terminal terminal, SearchSession session)
    {
        _terminal = terminal;
        _session = session;

        _terminal.WriteConsole(new StringBuilder().Insert(0, "\r\n", SearchSession.MaxVisible + 2).ToString());
        // Captured AFTER writing the reservation blank lines above (not before), since writing them can
        // scroll the buffer -- capturing earlier caused an ArgumentOutOfRangeException on a second run in
        // the same console window in an earlier version of this tool.
        _originTop = Math.Max(0, terminal.GetConsoleCursorRow() - (SearchSession.MaxVisible + 2));
    }

    // Builds the ENTIRE frame (input line + all rows + status line + final cursor position) as one
    // string, positioning each piece with ANSI cursor-addressing escapes (\x1b[{row};{col}H, 1-indexed)
    // instead of Console.SetCursorPosition, then flushes it with a single WriteConsole call. An earlier
    // version issued a separate SetCursorPosition + "clear this line" (\x1b[2K) + content Write per row --
    // ~45 small syscalls per keystroke/navigation, each of which classic conhost (cmd.exe, unlike Windows
    // Terminal) can paint before the next one arrives, which is what actually read as flicker: every row
    // going briefly blank between its clear and its rewrite. Padding each line to the terminal's own width
    // and writing it in one shot removes both the separate clear step and the many small writes. ANSI CUP
    // also can't throw on an out-of-range row (a real terminal just clips it), so no bounds-checked
    // fallback is needed the way a raw SetCursorPosition call would need.
    public void Render()
    {
        var width = Math.Max(1, _terminal.GetConsoleWidth() - 1);
        string Pad(string s) => s.Length >= width ? s[..width] : s.PadRight(width);

        var sb = new StringBuilder();

        sb.Append($"\x1b[{_originTop + 1};1H").Append(Pad("> " + _session.Query));

        for (var row = 0; row < SearchSession.MaxVisible; row++)
        {
            sb.Append($"\x1b[{_originTop + 2 + row};1H");
            var i = _session.ViewOffset + row;
            if (i >= _session.Results.Count)
            {
                sb.Append(Pad(""));
                continue;
            }

            var (r, highlights) = _session.Results[i];
            // No leading pointer glyph: the highlighted row's own reverse-video wrap below already marks
            // it unambiguously, and a ">" pointer here duplicated the input line's own "> " prompt right
            // above it visually -- two ">" characters stacked a row apart read as one broken/doubled
            // prompt rather than two distinct pieces of UI. The marker column is kept (still meaningfully
            // different: which rows are Tab-selected, independent of which one is currently highlighted).
            var isMarked = _session.Selected.Contains(r.Path);
            var marker = isMarked ? "*" : " ";
            var plain = $" {marker} {r.Path}";
            var padded = Pad(plain);
            // A marked (cyan-wrapped) row needs the highlight span to restore to cyan afterward, not the
            // terminal default, or the rest of that row would go blank/white past the match.
            var line = ApplyHighlight(padded, r.Name, plain.Length - r.Name.Length, highlights, isMarked ? "\x1b[36m" : "\x1b[39m");
            if (i == _session.HighlightIndex)
                sb.Append("\x1b[7m").Append(line).Append("\x1b[27m");
            else if (isMarked)
                sb.Append("\x1b[36m").Append(line).Append("\x1b[0m");
            else
                sb.Append(line);
        }

        var rangeText = _session.Results.Count == 0 ? "" : $" ({_session.ViewOffset + 1}-{Math.Min(_session.ViewOffset + SearchSession.MaxVisible, _session.Results.Count)} of {_session.Results.Count})";
        // Selections persist across query changes and can easily scroll out of the CURRENT result set
        // entirely (e.g. marked under one query, now typing a totally different one) -- without this,
        // there'd be no visible sign a Tab press earlier actually stuck, since none of the marked rows
        // are on screen.
        var selectionText = _session.Selected.Count > 0 ? $" [{_session.Selected.Count} selected]" : "";
        sb.Append($"\x1b[{_originTop + SearchSession.MaxVisible + 2};1H\x1b[90m").Append(Pad(_session.StatusLine + rangeText + selectionText)).Append("\x1b[0m");

        sb.Append($"\x1b[{_originTop + 1};{3 + _session.CursorPos}H"); // real terminal caret at the actual edit position, not always the end

        _terminal.WriteConsole(sb.ToString());
    }

    // `line` is the already width-padded/truncated PLAIN row text (" " + marker + " " + Path);
    // `nameStartInLine` is where the filename (r.Name, the tail of r.Path) begins within it. `highlights`
    // is the flat (start,length) pair sequence AppSearchPipeService computed server-side for this exact
    // result (via FuzzyMatcher.ComputeHighlightMask, run where AliasProviderRegistry's plugins are
    // actually loaded) and sent over the wire -- this function no longer computes anything itself, just
    // paints what the server already decided matched, which is what makes a pinyin/alias hit highlight
    // correctly here too.
    //
    // A plain foreground-color toggle (SGR 33 on / a caller-supplied restore code off), not bold (SGR 1):
    // bold used to be the toggle here, but on Windows' classic console (conhost, e.g. cmd.exe) SGR 1
    // doesn't render as a heavier glyph -- it flips the foreground's "intensity" bit, and combined with
    // the current row's own \x1b[7m reverse-video wrap (the highlighted row, above) that intensity bit
    // ends up on the swapped-in BACKGROUND instead, painting a solid lighter block behind the matched
    // characters rather than just tinting their text. A foreground color has no such interaction with
    // reverse video or with a marked row's own cyan wrap -- it just recolors the glyph -- so
    // `restoreColor` lets the caller say what color to return to (cyan for a marked row, default for
    // everything else) instead of this function guessing. Works purely on the already-truncated plain
    // string, never re-truncates -- avoids ever needing to count "visible" length through embedded escape
    // codes.
    private static string ApplyHighlight(string line, string name, int nameStartInLine, int[] highlights, string restoreColor)
    {
        if (highlights.Length == 0 || string.IsNullOrEmpty(name) || nameStartInLine < 0)
            return line;

        bool InRange(int idx)
        {
            for (var i = 0; i < highlights.Length; i += 2)
                if (idx >= highlights[i] && idx < highlights[i] + highlights[i + 1])
                    return true;
            return false;
        }

        const string HighlightColor = "\x1b[33m"; // yellow -- distinct from the cyan used for marked rows and the plain default foreground

        var sb = new StringBuilder(line.Length + 16);
        var lit = false;
        for (var c = 0; c < line.Length; c++)
        {
            var maskIdx = c - nameStartInLine;
            var shouldLight = maskIdx >= 0 && InRange(maskIdx);
            if (shouldLight != lit)
            {
                sb.Append(shouldLight ? HighlightColor : restoreColor);
                lit = shouldLight;
            }
            sb.Append(line[c]);
        }
        if (lit) sb.Append(restoreColor);
        return sb.ToString();
    }

    // \x1b[?25h re-shows the cursor (it's never explicitly hidden, but this is cheap insurance) and the
    // CUP escape parks the real terminal caret just below the drawn UI block, both via the same CONOUT$
    // handle Render() itself writes through -- not Console.SetCursorPosition/Console.CursorVisible, which
    // would throw once stdout (not this handle) is redirected.
    public void Cleanup() => _terminal.WriteConsole($"\x1b[{_originTop + SearchSession.MaxVisible + 2};1H\x1b[?25h");
}
