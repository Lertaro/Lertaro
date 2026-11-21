using Lertaro.Cli.Search;
using Lertaro.Cli.Space;
using Lertaro.Cli.Ui;
using Lertaro.Core.Services.Installation;
using System.Text;

// Interactive file search through the running App's search pipe (AppSearchPipeService).
// Type to search; use arrows or PageUp/PageDown to navigate, Tab to select multiple rows,
// and Enter to print the selected paths to stdout and exit.
//
// Terminal handles console I/O, AppSearchPipeClient handles the protocol, SearchSession owns search
// and selection state, and Renderer draws that state. This entry point runs the input loop.
//
// args[0] supplies the initial query. Otherwise, redirected stdin supplies its first non-empty line.
// Stdin is query text, not a candidate list. Both inputs start an ordinary App search; selection and
// printing still require user input. The installer adds the executable's directory to PATH.

if (SpaceEntriesCommand.IsRequested(args))
{
    if (!SpaceEntriesCommand.TryGetDirectory(args, out var directory))
    {
        Console.Error.WriteLine("Usage: lff --space-entries <directory>");
        Environment.ExitCode = 1;
        return;
    }

    var spacePipeName = AppSearchPipeClient.PipeNameFor(CurrentUserIdentity.SessionHash);
    try
    {
        var entries = await AppSearchPipeClient.GetSpaceEntriesAsync(spacePipeName, directory);
        Console.OutputEncoding = new UTF8Encoding(false);
        await Console.Out.WriteAsync(SpaceEntriesJsonFormatter.Serialize(entries));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[error] Could not read indexed space entries ({ex.GetType().Name}: {ex.Message}) -- is the Lertaro App running?");
        Environment.ExitCode = 1;
    }
    return;
}

if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Usage: lff [query]");
    Console.WriteLine("       lff --search <query> [--limit N] [--json] [--files|--folders] [--path <directory>]");
    Console.WriteLine("       lff --space-entries <directory>");
    Console.WriteLine();
    Console.WriteLine("Non-interactive search returns matching paths, or one JSON array with --json.");
    return;
}

if (args.Length == 1 && string.Equals(args[0], "--version", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine($"lff {typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown"}");
    return;
}

if (NonInteractiveSearchParser.TryParse(args, out var nonInteractiveOptions, out var searchError))
{
    if (nonInteractiveOptions == null)
    {
        Console.Error.WriteLine($"[error] {searchError}");
        Console.Error.WriteLine("Use 'lff --help' for usage.");
        Environment.ExitCode = 2;
        return;
    }

    Environment.ExitCode = await NonInteractiveSearchCommand.RunAsync(nonInteractiveOptions);
    return;
}

var pipeName = AppSearchPipeClient.PipeNameFor(CurrentUserIdentity.SessionHash);

try
{
    await AppSearchPipeClient.ProbeAsync(pipeName);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[error] Could not reach the Lertaro App's search pipe within {AppSearchPipeClient.ConnectTimeoutMs}ms ({ex.GetType().Name}: {ex.Message}) -- is the Lertaro App running?");
    Environment.ExitCode = 1;
    return;
}

var terminal = Lertaro.Cli.Terminal.Terminal.TryOpen();
if (terminal == null)
{
    // TryOpen already wrote the specific reason to stderr
    Environment.ExitCode = 1;
    return;
}

var session = new SearchSession(pipeName);
var renderer = new Renderer(terminal, session);
session.Changed += renderer.Render;

// Ctrl+C terminates the process either way (that's the point) -- this just gets the cursor parked back
// below the drawn UI and made visible first, instead of leaving the terminal wherever mid-render the
// frame happened to be when the signal landed.
Console.CancelKeyPress += (_, _) => renderer.Cleanup();

renderer.Render();

// args[0] wins if given; otherwise fall back to stdin's first line, if it was redirected.
var initialQuery = args.Length > 0 && !string.IsNullOrEmpty(args[0]) ? args[0] : ReadInitialQueryFromStdin();
if (!string.IsNullOrEmpty(initialQuery))
{
    session.Query.Append(initialQuery);
    session.CursorPos = session.Query.Length;
    session.TriggerSearch();
}

while (true)
{
    if (!terminal.TryReadKey(out var key))
        break; // the console input handle itself is gone (e.g. its window was closed) -- nothing left to read

    if (key.Key == ConsoleKey.Escape)
    {
        renderer.Cleanup();
        return;
    }

    if (key.Key == ConsoleKey.Enter)
    {
        renderer.Cleanup();
        foreach (var path in session.GetChosenPaths()) Console.WriteLine(path);
        return;
    }

    if (key.Key == ConsoleKey.Tab)
    {
        session.ToggleSelectionAtHighlight();
        renderer.Render();
        continue;
    }

    if (key.Key == ConsoleKey.DownArrow) { session.MoveHighlight(1); renderer.Render(); continue; }
    if (key.Key == ConsoleKey.UpArrow) { session.MoveHighlight(-1); renderer.Render(); continue; }
    if (key.Key == ConsoleKey.PageDown) { session.MoveHighlight(SearchSession.MaxVisible); renderer.Render(); continue; }
    if (key.Key == ConsoleKey.PageUp) { session.MoveHighlight(-SearchSession.MaxVisible); renderer.Render(); continue; }

    // Left/Right/Home/End move the text cursor within the query -- matches the split the real Lertaro
    // search box itself uses (Up/Down for result navigation, Left/Right for caret movement) -- and don't
    // touch the result list or trigger a new search at all, just re-render to reposition the caret.
    if (key.Key == ConsoleKey.LeftArrow) { session.CursorPos = Math.Max(0, session.CursorPos - 1); renderer.Render(); continue; }
    if (key.Key == ConsoleKey.RightArrow) { session.CursorPos = Math.Min(session.Query.Length, session.CursorPos + 1); renderer.Render(); continue; }
    if (key.Key == ConsoleKey.Home) { session.CursorPos = 0; renderer.Render(); continue; }
    if (key.Key == ConsoleKey.End) { session.CursorPos = session.Query.Length; renderer.Render(); continue; }
    if (key.Key == ConsoleKey.Delete)
    {
        if (session.CursorPos >= session.Query.Length) continue;
        session.Query.Remove(session.CursorPos, 1);
    }
    else if (key.Key == ConsoleKey.Backspace)
    {
        if (session.CursorPos == 0) continue;
        session.Query.Remove(session.CursorPos - 1, 1);
        session.CursorPos--;
    }
    else if (!char.IsControl(key.KeyChar))
    {
        session.Query.Insert(session.CursorPos, key.KeyChar);
        session.CursorPos++;
    }
    else
    {
        continue;
    }

    session.TriggerSearch();
}

// stdin isn't used for keyboard input at all (Terminal reads raw keystrokes off a directly-opened
// CONIN$ handle instead) -- if it's redirected, its first non-empty line is read here, once, eagerly
// (Console.In is a one-shot forward-only stream over whatever fed it -- there's nothing to re-read
// later) and used as a fallback initial query, the same way args[0] is. Not a candidate list: the search
// itself is still the normal App-pipe filesystem search.
static string? ReadInitialQueryFromStdin()
{
    if (!Console.IsInputRedirected)
        return null;
    while (Console.In.ReadLine() is { } line)
    {
        var trimmed = line.Trim(' ', '\t');
        if (trimmed.Length > 0)
            return trimmed;
    }
    return null;
}
