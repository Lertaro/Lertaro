# Instant Answers & Keyword Features

Beyond local file search, Lertaro includes a powerful suite of instant calculations, system tools, and keyword-triggered plugin extensions. Answers appear instantly without waiting for file search results.

## 1. Always Active Instant Answers

These features do not require any trigger prefix; they activate automatically whenever the input matches their patterns:

### Calculator & Base Conversion

Type any arithmetic expression directly into the search bar. Results appear in real time. Press `Enter` to copy the evaluated result to the clipboard:

```text
12 * (4 + 3)
100 * (1 - 0.15)
```

Common number base conversions are supported out of the box:

```text
255 to hex
0xFF to dec
101010 to bin
```

### Environment Variable Expansion & Inspection

- **Expand Variables**: Type `%NAME%` (e.g. `%PATH%`, `%APPDATA%`) to inspect its evaluated value. Multi-path variables such as `PATH` are split into clear line-by-line lists for inspection.
- **Fuzzy Search Variables**: Type `%` followed by a keyword (e.g. `%temp`) to fuzzy-search across all system and user environment variables.

### Quick Command Execution

Run commands directly without launching a terminal window first:

- `#<command>`: Opens a command prompt and executes the command **with Administrator privileges** (e.g. `#sfc /scannow` or `#net start Lertaro.Service`).
- `$<command>`: Opens a command prompt and executes the command with **standard user permissions** (e.g. `$ping 1.1.1.1` or `$ipconfig /all`).

### Direct URL Navigation

Type or paste any URL beginning with `http://` or `https://` and press `Enter` to open it immediately in your default browser.

## 2. Keyword-Triggered Plugin Extensions

By typing a short **trigger keyword + space** followed by your query, you can invoke dedicated plugin capabilities. All keywords can be customized under [**Settings → Plugins**](./settings/plugins).

| Default Keyword | Plugin Name | Description & Use Case | Example Usage |
| :--- | :--- | :--- | :--- |
| `ps` | **Process Manager** | Search running processes by name, PID, or window title (supports pinyin). Press Enter to terminate. | `ps chrome` or `ps 1234` |
| `win` | **Window Switcher** | Search and jump to active application windows with live background thumbnail snapshots. | `win code` or `win browser` |
| `bm` | **Browser Data** | Search bookmarks and history from Chrome, Edge, and Firefox profiles (history and bookmarks toggleable independently). | `bm github` |
| `set` | **Settings Search** | Fuzzy-search Lertaro's internal settings. Selecting an item jumps directly to that setting page and highlights it. | `set hotkey` or `set fuzzy` |
| `flow` | **Flow Launcher Bridge** | Lists loaded Flow.Launcher plugins and their action keywords, connecting with the Flow community ecosystem. | `flow` |

## 3. Web Search Engines

The Web Search plugin provides built-in shortcuts for major search engines. Type the prefix followed by your query to search in your default browser:

| Shortcut | Search Engine | Example | Description |
| :--- | :--- | :--- | :--- |
| `bd` | Baidu | `bd deep learning` | Search via Baidu |
| `g` | Google | `g lertaro github` | Search via Google |
| `bing` | Bing | `bing microsoft docs` | Search via Bing |
| `gh` | GitHub | `gh Lertaro` | Search directly across GitHub repositories |
| `wiki` | Wikipedia | `wiki quantum computing` | Look up Wikipedia entries |
| `yt` | YouTube | `yt lofi hip hop` | Search YouTube videos |

You can add, edit, or remove search engines and custom URL templates under **Settings → Plugins → Web Search → Configure**.

## 4. Instant Translation

Type the default trigger `tr` followed by text to translate it automatically into the language currently selected in Lertaro's interface:

```text
tr Hello, how are you today?
```

- Results appear asynchronously as you type; press `Enter` to copy the translation.
- Customize the trigger keyword under **Settings → Plugins → Translator → Configure**.

## 5. File Filters

Under **Settings → Plugins → File Filters → Configure**, you can create isolated search scopes for specific folders and extensions:

- **Monitored Folders**: Recursively scan specific working directories (e.g. `D:\Engineering\Drawings`).
- **Matching Rules**: Specify patterns such as `*.dwg;*.dxf` or `*.pdf;*.docx`.
- **Trigger Keyword**: Assign an exclusive keyword (e.g. `cad`), allowing you to isolate searches (e.g. `cad bracket_assembly`).

## 6. Custom Commands

Under **Settings → Plugins → Custom Commands → Configure**, wrap complex scripts, tools, or applications into concise commands:

- **Parameter Placeholders**: Supports positional placeholders `%s1`, `%s2`... and full query capture `%s`.
- **Quick Navigation Integration**: Check "Show in Quick Navigation" to pin the command directly into the [**Quick Navigation**](./hotkeys#3-quick-navigation-mouse-triggers) menu, with optional `/` submenu paths (e.g. `DevTools/RestartService`).
