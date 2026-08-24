# CLI Search (lff)

Lertaro includes a lightweight and highly efficient command-line companion named **`lff`** (Lertaro Fuzzy Finder) — an interactive fuzzy finder built specifically for terminal power users and shell scripts. It communicates via local named pipes with the running Lertaro App to reuse the in-memory index tree without rescanning drives.

## 1. Why Choose lff

- **Shared Index, Zero I/O Latency**: Unlike `fzf` or `find` that crawl disk sectors from scratch on every run, `lff` queries Lertaro's multimillion-item memory index tree in sub-milliseconds.
- **Identical Search Syntax**: Inherits Lertaro's fuzzy jump matching, Chinese pinyin aliases, and modifier operators (see [**Search Syntax**](./search-syntax)).
- **Fast Fail-Safe**: Automatically detects whether Lertaro App is running. If not, it prints a clear error message to `stderr` and exits immediately without hanging your terminal.

## 2. Installation & PATH Setup

`lff.exe` ships directly inside the Lertaro package:

- **Installer**: Check **Add lff CLI search tool to PATH** in the setup wizard to make `lff` accessible anywhere in your terminal.
- **Portable Edition**: Manually add the unzipped folder path to your user or system `PATH` environment variable.

> [!NOTE]
> After updating PATH, open a new terminal window for the change to take effect; existing shell sessions will not reflect environment changes automatically.

## 3. Interactive UI & Keybindings

Run `lff` in any terminal to open the full-screen interactive interface:

```bash
lff
```

### Keybindings Cheat Sheet

| Key | Description |
| :--- | :--- |
| **Type Characters** | Filters search results in real time with fuzzy jump matching. |
| `↑` / `↓` | Moves the highlight up / down. |
| `Page Up` / `Page Down` | Scrolls up / down by full page. |
| `←` / `→` | Moves the cursor horizontally within the search input bar. |
| `Tab` | Toggles selection/mark on the highlighted row (marked items display a `*` badge). |
| `Enter` | Outputs all marked paths (or the highlighted path if none marked) to `stdout` and exits. |
| `Esc` or `Ctrl+C` | Exits cleanly without outputting anything. |

## 4. Pre-filling Queries & Pipeline Input

You can provide an initial search term directly via command-line arguments or standard input:

```bash
# Via argument
lff report

# Via standard input pipeline
echo report | lff
```

Both open the interactive TUI pre-populated with `report` as the initial filter, allowing you to refine the search or press `Enter` directly to confirm.

## 5. Multi-selection & Batch Output

Press `Tab` to mark items. **Marked selections persist even when you change your search query**.

You can search for `doc` to mark several Word documents, clear the query, search `pdf` to mark several reports, and press `Enter` — `lff` outputs all marked paths across queries line-by-line to standard output.

## 6. Shell Scripting Examples

`lff` renders its TUI directly into the console buffer without polluting the standard output stream. Only the final confirmed path strings are written to `stdout`, making it ideal for piping into other tools:

### PowerShell Workflows

```powershell
# Open the selected file in VS Code
code (lff)

# Assign chosen directory to a variable and navigate to it
$target = lff; cd $target

# Pipe results as FileInfo objects down the pipeline
lff | Get-Item | Select-Object Name, Length, LastWriteTime
```

### CMD / Batch Workflows

```cmd
:: Process selected paths line-by-line in a for loop
for /f "delims=" %i in ('lff') do code "%i"
```

## 7. Limitations & Intentional Choices

- **Requires Foreground App**: `lff` relies on the running Lertaro App instance for querying; it does not perform standalone indexing.
- **No GUI Previews**: Optimized exclusively for fast terminal piping. For interactive media/rich text previews, use Lertaro's GUI [**Actions & Preview**](./actions-and-preview).
