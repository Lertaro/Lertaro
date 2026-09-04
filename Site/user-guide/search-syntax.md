# Search Syntax

Lertaro's search bar supports far more than simple plain-text search. Equipped with a blazing-fast matching algorithm, it supports fuzzy jump matching, boolean logic, word boundary operators, drive and path scoping, secondary filtering query tokens, and multilingual aliases. All syntaxes can be freely mixed within the same query.

## 1. Basic Matching & Case Sensitivity

### Fuzzy matching (default)

Lertaro enables Fuzzy Matching by default. Simply enter any characters in order, and it will match even if the characters are scattered across the file or folder name:

| Input Example | Matching Result | Description |
| :--- | :--- | :--- |
| `ltro` | `Lertaro.exe` | Characters match sequentially: `l` → `t` → `r` → `o` (**L**er**t**a**ro**.exe) |
| `vsc` | `Visual Studio Code.lnk` | Matches initial letters of each word (**V**isual **S**tudio **C**ode) |
| `rt-fin` | `Q3-report-final.docx` | Matches contiguous substring (Q3-repo**rt-fin**al.docx) |

Turn this off under **Settings → General → System → Enable fuzzy matching** and plain search terms (without operators) will require a contiguous substring — `abc` will only match names containing contiguous `abc`, no longer matching `a-b-c`. This toggle only affects bare terms; all operators described below maintain their exact behaviors either way.

### Smart Case Sensitivity

- **All-lowercase queries**: Case-insensitive by default. For example, `myfile` matches `myfile`, `MyFile`, `MYFILE`, etc.
- **Queries with uppercase letters**: Once a term contains any uppercase letter, it automatically becomes **case-sensitive**. For example, `MyFile` matches `MyFile`, but not lowercase `myfile`.

## 2. Multiple Terms & Boolean Logic

### Space: AND

Separate multiple search terms with spaces to require all conditions to be met. The order in which terms appear in the filename **does not matter**:

```text
report final 2024
```

The above query matches both `2024-Q3-report-final.docx` and `final_report_2024.pdf`.

### Pipe `|`: OR

Use a pipe symbol `|` to separate terms where matching any single alternative is sufficient:

```text
png | jpg | gif
```

You can freely combine AND and OR logic:

```text
report | summary 2024
```

This finds files matching either `report` or `summary`, and also containing `2024`. In OR queries, all matched terms across hit branches are highlighted simultaneously in the result name.

### Operator Precedence: OR binds tighter than AND

When spaces (AND) and the pipe `|` (OR) are mixed in a single query, `|` has **higher** precedence than spaces: the terms on both sides of `|` are merged into one OR group first, and the space-separated groups are then ANDed together. Parentheses are not supported, so this binding order cannot be changed.

```text
report | summary 2024 | draft
```

is equivalent to `(report OR summary) AND (2024 OR draft)`.

Note: `|` must be a standalone token with spaces on both sides — `a|b` or `a |b` is not parsed as OR. Do not put an `!` exclusion term inside a `|` OR group either (e.g. `b | !c`), which is parsed as "b matches or c does not"; to exclude a term globally, give it its own space-separated AND condition instead (e.g. `b !c`).

### Escaping Spaces & Quoted Phrases

To search for a phrase containing spaces within a single term, escape the space with a backslash `\ `, or enclose the phrase in single quotes `'...'` or double quotes `"..."`:

```text
final\ report
'final report'
```

Both examples treat `final report` as a single phrase with space, rather than splitting it into two independent AND terms.

### Pasting Multiple Lines Folded into OR

When copying multi-line text (such as filenames from a spreadsheet, text file, or log) and pasting it directly into the search bar, Lertaro automatically folds the lines into a single OR query separated by `|` (blank lines are automatically skipped):

```text
123
456
678
```

Pastes automatically as:

```text
123 | 456 | 678
```

## 3. Search Operators Cheat Sheet

### Operators Table

| Operator / Syntax | Type | Description | Input Example | Matching Result Example |
| :--- | :--- | :--- | :--- | :--- |
| *(none)* | Default Fuzzy | Characters appear in order anywhere in name (when fuzzy is on) | `report` | `Q3-report-final.docx` |
| `!` | Exclude | Excludes all results whose name contains this exact substring | `!temp` | Filters out files containing `temp` |
| `'` | Flip Exactness | Exact substring when fuzzy is on; fuzzy when fuzzy is off | `'report` | Must contain contiguous substring `report` |
| `'...'` | Word Boundary | Exact substring matched on word boundaries (not inside larger word) | `'app'` | Matches `app.exe`, `my-app.log`; does not match `whatsapp.exe` |
| `^` | Prefix Match | Name must start with this text | `^IMG` | `IMG_20240101.jpg` (does not match `MY_IMG.jpg`) |
| `$` | Suffix Match | Name must end with this text | `.pdf$` | `document.pdf` (does not match `document.pdf.bak`) |
| `^...$` | Exact Match | Name must equal this text exactly | `^readme.md$` | Matches only `readme.md` |
| `\|` | OR Logic | Matches either side of the pipe | `doc \| pdf` | Matches names containing `doc` or `pdf` |

### Detailed Operator Behaviors & Combinations

1. **Exclusion `!`**: `!term` directly excludes results containing `term` as an exact substring. Excluded terms do not undergo pinyin/alias expansion to avoid over-exclusion.
2. **Exactness Flip `'`**: When global fuzzy matching is enabled, prefixing `'` forces a specific term to match as an exact substring.
   - For example, `lertaro 'v1.2` performs a fuzzy search for `lertaro` while requiring `v1.2` as an exact contiguous substring.
   - Exact terms still match filenames that carry pinyin aliases: `'exe$` also finds `古恩希尔`, whose pinyin maps to `gexe`.
3. **Word Boundary Exact Match `'...'`**: Enclosing a single word in quotes (e.g. `'app'`) checks for word boundaries (spaces, punctuation, hyphens, underscores, or string boundaries), preventing false positives inside larger words.
4. **Exact Match `^...$`**: Only applies when `^` and `$` wrap the **same word**. When used on separate words (e.g. `^src md$`), they remain separate prefix and suffix filters.

**Operator Combination Examples**:

- `^IMG !.png$ 2024`: Finds files starting with `IMG`, containing `2024`, and **not** ending in `.png`.
- `'data | 'backup ^2024 .zip$`: Finds archives starting with `2024`, ending in `.zip`, and containing the exact substring `data` or `backup`.
- `^report '公告 | 'gw .pdf$ !draft`: Finds names starting with `report`, ending in `.pdf`, without `draft`, and exactly containing `公告`, `gw`, `公文`, or other text combinations matching the `gw` pinyin (`'公告 | 'gw` forms one OR group; the remaining space-separated conditions are ANDed with it).

## 4. Path Mode & Drive Scoping

### Targeting a Drive

Start your query with a drive letter followed by a colon to restrict results strictly to that drive:

```text
d: report
```

The space is optional: `d:report` and `d: report` are identical.

### Full Path Mode

When your search query contains path separators (`\` or `/`), Lertaro automatically switches to full path matching mode:

```text
D:\Projects\Lertaro
```

Ending with a path separator (e.g. `D:\Projects\`) searches the direct contents **inside** that folder.

### Folder Matching Fallback

When searching by filename alone does not fill the result capacity, Lertaro automatically uses query terms not matched in the filename to match ancestor folder names without requiring special syntax:

```text
d01j dcj
```

Even if `dcj` never appears in the file's own name, Lertaro finds `d01j.txt` located in a folder named (or aliased to) `dcj`.

> [!NOTE]
> This requires at least one term to match the filename itself, and only triggers when name-only matches have not filled the results. Fallback results are always ranked after direct filename matches.

## 5. Query Tokens & Secondary Filtering

Lertaro supports appending **Query Tokens** guided by a colon prefix `:` (customizable in **Settings → General → System → Query Token Global Prefix Character**) to perform chained secondary filtering and sorting on primary results.

Multiple tokens can be combined in a single `:` suffix separated by commas `,`, such as `report :@doc,M-,:-F`.

### Category Filters (`:@<category>`)

Quickly apply preset file extension category rules, supporting `|` combinations:

- `:@doc`: Documents (`*.doc; *.docx; *.pdf; *.txt; *.ppt; *.pptx; *.xls; *.xlsx; *.csv; *.rtf; *.md; *.wps`)
- `:@img`: Images (`*.jpg; *.jpeg; *.png; *.gif; *.bmp; *.webp; *.ico; *.svg; *.tif; *.tiff; *.psd; *.ai`)
- `:@video`: Videos (`*.mp4; *.mkv; *.avi; *.mov; *.wmv; *.flv; *.m4v; *.webm; *.3gp; *.rmvb; *.ts`)
- `:@audio`: Audio (`*.mp3; *.wav; *.flac; *.aac; *.ogg; *.m4a; *.wma; *.ape`)
- `:@zip`: Archives (`*.zip; *.rar; *.7z; *.tar; *.gz; *.bz2; *.xz; *.iso`)

**Examples**:

- `financial :@doc`: Search for "financial" among documents.
- `wallpaper :@img`: Search for "wallpaper" among images.
- `clip :@video|audio`: Search for "clip" among videos or audio files.

You can customize rules or add new categories under **Settings → Plugins → CoreExtensions**.

### Specific Extension Filters (`:.ext` or `:.ext1.ext2`)

Use a dot prefix to specify one or more file extensions (automatically excludes folders):

- `report :.pdf`: Retains only `.pdf` files.
- `data :.csv.xlsx`: Retains only `.csv` or `.xlsx` spreadsheet files.

### Result Sorting & File/Folder Filters (`:[SCMAF]`)

Use single letters to specify sorting attributes: `S` (Size), `C` (Created time), `M` (Modified time), `A` (Accessed time), `F` (Folder/File filter).

The bare letter indicates **ascending order** (smallest / oldest first); adding a minus `-` (as a prefix or suffix, e.g. `M-` or `:-M`) indicates **descending order** (largest / newest first) or inverted filtering:

| Token Syntax | Effect | Typical Use Case |
| :--- | :--- | :--- |
| `:S` | Sort by file size ascending (smallest first) | Locate empty or tiny files |
| `:S-` or `:-S` | Sort by file size descending (largest first) | `log :S-` (troubleshoot massive log files) |
| `:M` | Sort by modified time ascending (oldest first) | Find stale, unmaintained files |
| `:M-` or `:-M` | Sort by modified time descending (newest first) | `report :M-` (find recently edited documents) |
| `:C` / `:C-` | Sort by creation time ascending / descending | `build :C-` (find latest build outputs) |
| `:A` / `:A-` | Sort by access time ascending / descending | `project :A-` (find recently opened projects) |
| `:F` | **Folders only** (filters out regular files) | `config :F` (find only directories named config) |
| `:-F` or `:F-` | **Files only** (filters out folders/directories) | `config :-F` (find only files named config) |

### Wildcard Secondary Filters (`:?<expression>` or `?<expression>`)

Use standard Windows wildcards (`?` for single character, `*` for zero or more characters) for precise matching, supporting `|` or `;` for multiple OR conditions:

- `mp4 :?(2026???????????)`: Matches video files containing `2026` followed by an 11-digit timestamp.
- `photo :?IMG_????.jpg|DSC_????.jpg`: Matches specific photo numbers across two camera formats.

### Path Segment Filters (`::<path-expression>`)

Requires ancestor directory names or the filename itself to match the specified fuzzy keyword:

- `report ::2024`: Requires the parent folder hierarchy to contain `2024`.
- `main ::"src\core"`: Requires files to be located under `src\core` and its subdirectories.

### Chained Query Token Examples

Tokens can be combined together after a single `:` prefix:

- `report :@doc,M-`: Searches "report", filters to documents, sorted by modified time descending (newest first).
- `backup :.zip,S-,:-F`: Searches "backup", filters to `.zip` archives, sorted by size descending, files only.
- `icon ::assets,?*128*`: Searches "icon", located under `assets` paths, with `128` size tags in the name.

## 6. Special Search Features

### Bypassing Exclusion Rules for One Search

Prefix a query with `*` to temporarily bypass user-configured path exclusions, globs, and regular expressions in [**Exclusion Rules**](./settings/index-drives#exclusion-rules) for this single search, without modifying settings:

```text
*node_modules
```

The leading `*` is stripped before matching. This only recalls already indexed files (excluded paths on network/WSL drives that were never indexed will not appear); system/hidden file filters remain active.

### Result Type Trigger

Under **Settings → General → Quick Search Window → Result Type Priority**, you can configure a single-character **trigger** for specific result types (Applications, Settings, File Categories, Plugins, Files, etc.).

Typing the trigger as the very first character in the quick search window displays only that result type, hiding all others:

```text
;vs
```

If `;` is assigned to "Applications", the above query searches Visual Studio exclusively among applications. In Quick and Inline search windows, History and Favorites remain pinned at the top regardless of triggers.

## 7. Multilingual Aliases

### Chinese filenames: pinyin aliasing

Bundled with the `PinyinAlias` plugin, Chinese filenames are searchable via pinyin out of the box with zero configuration:

- **Full Pinyin**: Typing `chongqing` matches `重庆.docx`.
- **Pinyin Initials**: Typing `cq` also matches `重庆.docx`; typing `wzry` matches `王者荣耀.exe`.
- **Polyphonic Characters**: Common pronunciations are automatically indexed (e.g. `重庆` matches both `chongqing` and `zhongqing`).

You can verify that `PinyinAlias` is active under **Settings → Plugins**.

### Spanish filenames: accent aliasing

Bundled with the `SpanishAlias` plugin, filenames containing Spanish accented characters (`á`, `é`, `í`, `ó`, `ú`, `ü`, `ñ`) can be searched seamlessly using unaccented ASCII letters:

- Typing `cancion` matches `Canción.mp3`.
- Typing `nino` matches `Niño.txt`.
- Typing `ciguena` matches `Cigüeña.png`.

Matched characters (including accented vowels in the original name) are accurately highlighted. Manage the plugin under **Settings → Plugins**.

## 8. FAQ & Favorites

### Favorites, not custom aliases

Lertaro does not provide a generic "custom search alias/macro" mechanism. The closest native solutions:

- [**Favorites**](./settings/favorites): pin any file, folder, or URL under a custom display name, making it searchable by that custom title (marked with a ★ icon in results).
- **File Filters** (see [**Instant Answers**](./instant-answers#file-filters)): bind a trigger keyword to chosen folders, then typing `keyword term` in the quick search window restricts a normal index search to those folders.

If you want to trigger custom scripts or launch programs using custom keywords, see [**Custom Commands**](./instant-answers#custom-commands).
