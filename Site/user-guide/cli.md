# Command-Line Search (lff)

Lertaro also ships a small command-line companion, **`lff`** — an fzf-style fuzzy finder that
searches through the same index the App itself already maintains, instead of duplicating any of
that setup. It's for anyone who lives in a terminal and wants Lertaro's search (fuzzy matching,
pinyin aliasing, network drives, everything) available there too, not just in the search windows.

`lff` needs the Lertaro App to already be running — it talks to the App over a local, per-user
pipe rather than re-scanning anything itself. If the App isn't running, `lff` fails fast with an
error on stderr instead of hanging.

## Setting it up

`lff.exe` is installed alongside the App. Check **Add the lff command-line search tool to PATH**
on the installer's task selection page to make it runnable as `lff` from any terminal — this adds
Lertaro's install folder to your system PATH. Open a new terminal window afterward; one already
open when you installed won't pick up the change.

If you skipped that option, you can still run it directly from wherever Lertaro is installed.

## Basic usage

```
lff
```

opens an interactive picker: type to fuzzy-filter, exactly like the App's own search — including
pinyin-alias matching for Chinese filenames (see [Search Syntax](./search-syntax)).

| Key | Action |
|---|---|
| Type | Filter results |
| ↑ / ↓ | Move the highlight |
| Page Up / Page Down | Jump a page at a time |
| ← / → | Move the text cursor within the query |
| Tab | Toggle-select the highlighted result (marked rows show `*`) |
| Enter | Print the selected path(s) — or just the highlighted one, if nothing's marked — and exit |
| Esc / Ctrl+C | Exit without printing anything |

## Pre-filling the query

```
lff report
```

and

```
echo report | lff
```

both open already filtered to `report`. Either way this only pre-fills the query box and starts
the same search typing it would — it never auto-selects or auto-prints a result on its own, so you
still navigate and press Enter/Tab yourself.

## Selecting multiple results

Tab marks or unmarks the highlighted row. Marked rows persist even after you change the query — so
you can search for one file, mark it, search for something else, mark that too, and so on. The
status line shows how many are currently marked. Pressing Enter while anything is marked prints
every marked path, one per line, regardless of what's currently highlighted.

## Using the result in another command

`lff`'s interactive picker is drawn directly to the console, never through the normal
input/output streams — the only thing that ever goes to stdout is the final selected path(s), one
per line, printed on Enter. That's what lets its output compose with the usual shell techniques
for capturing another command's result.

PowerShell:

```powershell
code (lff)
$path = lff; code $path
```

cmd.exe (no built-in command substitution — use `for /f`):

```cmd
for /f "delims=" %i in ('lff') do code "%i"
```

## Limitations

- No preview pane — deliberately out of scope, since the App's own [Actions Menu &
  Preview](./actions-and-preview) already covers that.
- Requires the Lertaro App to be running; `lff` doesn't index anything on its own.
