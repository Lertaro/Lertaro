using Lertaro.Cli.Search;
using Lertaro.Cli.Space;
using Lertaro.Cli.Ui;
using Lertaro.Core.Services.Installation;
using System.Text;

// An fzf-style CLI: instead of replicating the App's own search initialization (plugin loading,
// UserNetworkDriveSearch.Configure()), this connects to a pipe the App itself hosts
// (AppSearchPipeService, App\Services\AppSearchPipeService.cs) and asks the already-running,
// already-initialized App to do the search. Type to fuzzy-filter, Up/Down or PageUp/PageDown to scroll,
// Tab to multi-select, Enter prints the selected path(s) to stdout and exits. No preview pane --
// deliberately out of scope. Requires the real Lertaro App to be running.
//
// The pieces are split out by concern (see their own file-header comments for the reasoning behind
// each): Terminal (Cli\Terminal\Terminal.cs) owns the raw CONOUT$/CONIN$ console I/O, AppSearchPipeClient
// (Cli\Search\AppSearchPipeClient.cs) owns the wire protocol to the App, SearchSession
// (Cli\Search\SearchSession.cs) owns the query/results/selection state and debounced search triggering,
// and Renderer (Cli\Ui\Renderer.cs) draws SearchSession's state onto a Terminal. This file just wires
// them together and runs the key-handling loop.
//
// Two fzf-shaped entry points, both resolved before the interactive loop starts, and both doing the SAME
// thing -- pre-filling the query box, nothing more:
//  - args[0], if present, becomes the initial query (`lff report` opens already filtered to "report").
//  - Otherwise, if stdin is redirected (`echo report | lff`), its first non-empty line becomes the
//    initial query instead. This is NOT a candidate list to filter over locally -- it's still a normal
//    App-pipe filesystem search, just with the keyword supplied a different way. args[0] wins if both
//    are given.
// Either way, this only ever pre-fills the query and fires the same search a keystroke would -- it never
// auto-selects or auto-prints a result. The user still navigates and presses Enter/Tab themselves, exactly
// as if they'd typed the query by hand.
//
// Getting onto PATH at all is the installer's job (Installer\installer.iss's "addtopath" task adds {app}
// -- where this exe already sits alongside Lertaro.App.exe -- straight to the machine's PATH), not this
// program's own -- there's no install/uninstall command here.

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
