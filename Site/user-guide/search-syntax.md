# Search Syntax

Lertaro's search bar supports far more than simple plain-text search. Equipped with a blazing-fast matching algorithm, it supports fuzzy jump matching, boolean logic, exclusion, drive and path scoping, sorting and filtering query tokens, and multilingual aliases. All syntaxes can be freely mixed within the same query.

## 1. Basic Matching & Case

### Fuzzy matching (default)

Lertaro enables Fuzzy Matching by default. Simply enter any characters in order, and it will match even if the characters are scattered across the file or folder name:

| Input Example | Matching Result | Description |
| :--- | :--- | :--- |
| `ltro` | `Lertaro.exe` | Characters match sequentially: `l` → `t` → `r` → `o` (**L**er**t**a**ro**.exe) |
| `vsc` | `Visual Studio Code.lnk` | Matches initial letters of each word (**V**isual **S**tudio **C**ode) |
| `rt-fin` | `Q3-report-final.docx` | Matches contiguous substring (Q3-repo**rt-fin**al.docx) |

Turn this off under **Settings → General → System → Enable fuzzy matching** and every plain term requires a contiguous substring — `abc` will only match names containing contiguous `abc`, no longer matching `a-b-c`. This toggle only affects ordinary terms: exclusions are always exact (see below), the token syntax is unaffected either way, and a single term can still be flipped to the opposite reading with a leading `?` (see below).

### Case Insensitivity

Matching always ignores case, in both directions — the case you type never changes what matches, and the case of the file name never does either. `myfile`, `MyFile` and `MYFILE` all match each other.

There is no case-sensitive mode: typing a capital never narrows a term to exact-case matches.

### Pinyin Aliases & Ranking

Chinese names are searchable by pinyin, in two forms: the **initials** (one letter per character — `ex` for 恶性) and the **full reading** (every syllable spelled out — `zhengshu` for 证书).

**Ranking: match position > coverage > English > initials > full pinyin.** A match starting further left wins first; then a tighter, fuller match; English/initials/full-pinyin only separate results that already agree on both of those. So an English match no longer automatically outranks a pinyin one — it does when the two are equally well placed and equally tight. A partial last syllable still matches, so `zhengsh` keeps finding 证书 while you are still typing.

**With fuzzy matching off**, pinyin matches must line up with word starts. `ex` finds 恶性 (initials of two characters) but not 学习 (which would have to splice the end of `xue` onto the start of `xi`). With fuzzy matching on, that loose reading is what you asked for and remains available.

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
report | summary
```

This finds files matching either `report` or `summary`. In OR queries, all matched terms across hit branches are highlighted simultaneously in the result name.

### Operator Precedence: AND binds tighter than OR

When spaces (AND) and the pipe `|` (OR) are mixed in a single query, the space binds **tighter** than the pipe by default: each space-separated run of terms is ANDed into its own group first, and those groups are then ORed together. Parentheses are not supported, and this is the standard boolean reading — but the old binding order is one toggle away (see below).

```text
report | summary 2024 | draft
```

is equivalent to `report OR (summary AND 2024) OR draft`.

This is the reading a user who types a few alternatives and then narrows one of them expects: `summary 2024` stays a single conjunction instead of dissolving into two independent alternatives.

#### Switching back to OR-first

Under **Settings → General → System → OR binds tighter than AND (legacy)** you can restore Lertaro's historical binding order, in which the pipe binds tighter than the space:

```text
report | summary 2024 | draft
```

then means `(report OR summary) AND (2024 OR draft)`.

The toggle only changes the precedence between the two operators — no new operator is introduced, and it has no effect on a query that uses only one of them (`read me` and `readme | rdm` mean the same thing under either setting).

Note: `|` must be a standalone token with spaces on both sides — `a|b` or `a |b` is not parsed as OR. This also applies to an exclusion: `b | :c` is read as "b, or not c", so to exclude something from the whole query, give the exclusion its own space-separated position instead (`b :c`).

#### Precedence is not affected by quoting

There is no quoting syntax that can change how precedence is read; grouping is fixed by the setting above. A `|` is only an OR when it is its own space-separated token, so putting one inside ordinary text such as `data|backup` simply searches for that literal string.

### The Space Rule

There is no way to put a space inside a single term. A space is always the AND separator, so a query with spaces is always several ANDed terms — this is the one place where Lertaro reads punctuation as structure:

```text
final report
```

is `final` AND `report`, which is not the same as a single phrase `final report`.

**Neither quoting nor a backslash solves this.** `'final report'` and `"final report"` are not phrase syntax: quotes are matched as literal characters, so those queries look for names containing a quote mark, and they find nothing. `final\ report` is likewise read as the two words `final` AND `report`.

The upshot is that Lertaro has no exact-phrase search. When the two words are adjacent in the name you want, search for the more distinctive half and let the ranking put it on top — or use a regex clause, which matches the name as one whole string (see below):

```text
/^final report/
```

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

| Operator / Syntax | Type | Description | Input Example |
| :--- | :--- | :--- | :--- |
| *(none)* | Default term | Fuzzy when fuzzy matching is on, exact substring when it is off | `report` |
| `:` | Exclusion | Drops every result whose name contains this text (always exact, never fuzzy) | `:temp` |
| `?` | Precision inversion | Flips the term between fuzzy and exact, the opposite of the current setting | `?report` |
| `\|` | OR logic | Matches either side of the pipe | `doc \| pdf` |
| `/.../` | Regular expression | Matches the name with a .NET regex (see below) | `/^report.*\.md$/` |
| `*` | Bypass exclusions | One-off opt-out of your configured exclusion rules (first character only) | `*node_modules` |
| `<` `>` | Sort / filter token | Sorts and optionally filters the results (see [section 5](#_5-query-tokens-sorting-filtering)) | `<s>20m` |
| `\` | Plugin token | Applies a plugin-provided filter, e.g. a file category (see [section 5](#_5-query-tokens-sorting-filtering)) | `\audio` |

### Detailed Operator Behaviors & Combinations

1. **Exclusion `:`** — `:term` drops every result whose name contains `term`. The exclusion is written **without a space** after the colon (`:temp`, not `: temp`), and it must not be the only thing in the query: because an exclusion can only remove results, a query of nothing but exclusions shows no results at all. In practice that means always keeping at least one ordinary term alongside it.
2. **Exclusions are always exact** — they are matched as a contiguous substring even when fuzzy matching is on, and they are not expanded through pinyin aliases. A loose subsequence or a pinyin spelling would otherwise remove files you never named.
3. **A lone colon is ignored** — `:` with nothing after it is not an operator; it is simply dropped from the query.
4. **The drive colon is different** — a drive letter follows the colon (`d:`, see [section 4](#_4-path-mode-drive-scoping)) while an exclusion precedes it (`:temp`). The two can never be confused, and a colon inside a word (`c:\path`) is ordinary text.
5. **A regex clause is lifted out before anything else reads the query** — that is what keeps its backslashes and slashes from being mistaken for a path, and it means a clause can sit anywhere in the query (`/\.md$/ report` and `report /\.md$/` are the same search).
6. **Precision inversion `?`** — `?term` takes the **opposite** of whatever the fuzzy-matching setting says: with fuzzy matching on the term becomes a contiguous substring, with fuzzy matching off it becomes a scattered subsequence. It is the only way to mix the two readings within one query — `?report draft` requires `report` contiguously while `draft` may be scattered. The trigger is read from the **first character of a word only**, so `rep?ort` is the literal text `rep?ort` (which cannot occur in a file name and therefore matches nothing), and it affects that word alone. Like a lone `:`, a lone `?` is dropped. The colon is read first, so `:?temp` excludes the literal text `?temp`.

**Operator Combination Examples**:

- `report :draft`: Finds names containing `report` and drops any whose name contains `draft`.
- `IMG :png :gif`: Finds names containing `IMG` while dropping both `png` and `gif` files. Both exclusions are ANDed — a name survives only if it contains neither.
- `log :temp :bak`: Keeps `log` files that are neither temp nor backup files.

### Regular Expressions (`/.../`)

Write a .NET regular expression between slashes to match a file **name** exactly as you describe it:

```text
/^report.*\.md$/
```

That finds names starting with `report` and ending in `.md`. The clause is ANDed with the rest of the query, so `report /\.pdf$/` keeps only PDFs among the `report` matches.

Four things are worth knowing:

- **It matches the name, not the path**, and not file contents. Use a path query for folders.
- **It does not go through pinyin aliases.** A regex describes the characters actually in the name, so a Chinese name is matched by its own characters, not by a pinyin spelling of them.
- **The slashes are the delimiter, and `\` escapes.** Write `\.` for a literal dot (`.` alone means any character). To match a literal slash, write `\/`. Because `/` is also Windows' alternate path separator, a clause has to be a whole word that both opens and closes with `/`, and an unescaped `/` inside it closes the clause — which is what keeps a forward-slash path such as `C:/Users/me`, `/mnt/c/Users` or `/usr/local/` an ordinary path rather than a regex. An unclosed clause is treated as ordinary text rather than swallowing the rest of the query.
- **A regex cannot be accelerated the way a term can**, because it is not a fixed string. Lertaro pulls the longest run of literal characters the expression requires — `.exe` from `/\.exe$/`, nothing at all from `/^(ogg|mp3)$/` — and uses that to skip most candidates before running the real expression. Adding an ordinary word alongside a literal-free regex is the reliable way to keep such a search fast.

## 4. Path Mode & Drive Scoping

### Targeting a Drive

Start your query with a drive letter followed by a colon to restrict results strictly to that drive:

```text
d: report
```

The drive spec must be a **standalone, two-character token**: the colon has to be followed by a space. `d:report` is no longer a drive spec — it is an ordinary term searching for the literal text `d:report`, because guessing a drive from the first two characters produced false positives. Only ASCII letters are recognized, so `中: x` is not a drive either.

### Full Path Mode

When your search query contains path separators (`\` or `/`), Lertaro automatically switches to full path matching mode:

```text
D:\Projects\Lertaro
```

Ending with a path separator (e.g. `D:\Projects\`) searches the direct contents **inside** that folder.

> [!NOTE]
> A plugin token also starts with `\` (for example `\audio`). Tokens are lifted out of the query before path mode is decided, so a query that only contains tokens and ordinary words (`report \audio`) stays a normal name search. Path mode only triggers when a separator is left in the remaining text.

### Folder Matching Fallback

When searching by filename alone does not fill the result capacity, Lertaro automatically uses query terms not matched in the filename to match ancestor folder names without requiring special syntax:

```text
d01j dcj
```

Even if `dcj` never appears in the file's own name, Lertaro finds `d01j.txt` located in a folder named (or aliased to) `dcj`.

> [!NOTE]
> This requires at least one term to match the filename itself, and only triggers when name-only matches have not filled the results. Fallback results are always ranked after direct filename matches.

## 5. Query Tokens: Sorting & Filtering

Query tokens are the words that start with a character that cannot occur in a Windows file name, so they can never be confused with text you want to search for. There are two families:

| Family | Trigger | Example | Owned by |
| :--- | :--- | :--- | :--- |
| Sort / filter | `<` and `>` | `<s>20m` | The `CoreExtensions` plugin |
| Plugin tokens | Your configured **Plugin Query Token Prefix** (default `\`) | `\audio` | Whichever plugin claims the token |

### Tokens work anywhere in the query

A token does **not** have to go at the end. Any of these are equivalent:

```text
report <s>20m \audio
<s>20m report \audio
\audio report <s>20m
```

A token only counts when it is the **whole word** and starts the word — `abc\def` is ordinary text, not a token.

### Sorting and Thresholds (`<` and `>`)

The first character picks the sort direction, the next letter picks the property:

| Token | Meaning |
| :--- | :--- |
| `<s` | Sort by size, smallest first |
| `>s` | Sort by size, largest first |
| `<c` / `>c` | Sort by creation time, oldest / newest first |
| `<m` / `>m` | Sort by modified time, oldest / newest first |
| `<a` / `>a` | Sort by accessed time, oldest / newest first |
| `<f` / `>f` | Sort files first / folders first |

Add a second trigger plus a threshold to keep only one side of it:

| Token | Meaning |
| :--- | :--- |
| `<s>20m` | Files larger than 20 MB |
| `<s<20m` | Files smaller than 20 MB |
| `>c>2008.8.3` | Items created after 2008-08-03 |

The second trigger is a **comparison**, not a repetition of the sort arrow: `>` always means a lower bound and `<` always means an upper bound.

Sizes accept `k`, `m`, `g` and `t` suffixes (binary units, so `1m` is 1 MiB) or a plain byte count. Thresholds for the folder/file key use `f` / `folder` / `dir`.

Dates must be written **year first**. These shapes are accepted (a two-digit year is read as `20xx`):

| Separator | Year width | Month/day width | Examples |
| :--- | :--- | :--- | :--- |
| `-` | 4 or 2 | padded or not | `2003-01-03`, `2003-1-3`, `03-1-3` |
| `.` | 4 or 2 | padded or not | `2003.01.03`, `2003.1.3`, `03.1.3` |
| `/` | 4 or 2 | padded or not | `2003/01/03`, `2003/1/3`, `03/1/3` |
| none | 4 | — | `20030103` |

Separators may not be mixed (`2003-08.03` is not a date), and the month-first reading `01-03-2003` is not accepted — the order is always year, month, day, so one query can never mean two different days on two machines. Year-only (`2008`) and year-month (`2008.8`) forms are also accepted, meaning "during 2008" and "during August 2008".

### Plugin Tokens (`\`)

Plugin tokens are provided by plugins, and the plugin decides what each one means. The bundled `CoreExtensions` plugin supplies file-category filters:

- `\doc`: Documents (`*.doc; *.docx; *.pdf; *.txt; *.ppt; *.pptx; *.xls; *.xlsx; *.csv; *.rtf; *.md; *.wps`)
- `\img`: Images (`*.jpg; *.jpeg; *.png; *.gif; *.bmp; *.webp; *.ico; *.svg; *.tif; *.tiff; *.psd; *.ai`)
- `\video`: Videos (`*.mp4; *.mkv; *.avi; *.mov; *.wmv; *.flv; *.m4v; *.webm; *.3gp; *.rmvb; *.ts`)
- `\audio`: Audio (`*.mp3; *.wav; *.flac; *.aac; *.ogg; *.m4a; *.wma; *.ape`)
- `\zip`: Archives (`*.zip; *.rar; *.7z; *.tar; *.gz; *.bz2; *.xz; *.iso; *.wim; *.esd`)

**Examples**:

- `financial \doc`: Search for "financial" among documents.
- `wallpaper \img`: Search for "wallpaper" among images.

Rename the categories, change which extensions each one covers, or add your own under **Settings → Plugins → CoreExtensions**. The keyword itself is matched longest-first, so a `\a` rule and an `\audio` rule can coexist and `\audio` still wins.

The prefix character is configurable under **Settings → General → System → Plugin Query Token Prefix**. It cannot be empty, it cannot be a character the search syntax already consumes (`\` `<` `>` `:` `*` `/` `?`), and it must not be the same character as another plugin's own prefix — settings report such a collision instead of letting one provider silently win.

The full search window's left type-filter sidebar is configured separately in the same plugin's **Search Filters** group. Sidebar filter names are display-only; prefix references are parsed only inside a sidebar filter rule and refer to keywords from the **Custom Filters** list, including disabled custom filters.

### Chained Tokens

Because tokens are independent words, several can appear in one query and each one applies in turn:

- `report \doc >m`: Searches "report", keeps documents only, sorted by modified time (newest first).
- `backup \zip <s`: Searches "backup", keeps `.zip` archives, sorted by size (smallest first).
- `icon \img <s>1m`: Searches "icon", keeps images larger than 1 MB, smallest first.

## 6. Special Search Features

### Bypassing Exclusion Rules for One Search

Prefix a query with `*` to temporarily bypass user-configured path exclusions, globs, and regular expressions in [**Exclusion Rules**](./settings/index-drives#_5-exclusions) for this single search, without modifying settings:

```text
*node_modules
```

The leading `*` is stripped before matching. This only recalls already indexed files (excluded paths on network/WSL drives that were never indexed will not appear); system/hidden file filters remain active. Note that this is the app's own exclusion **rules**, which is a different thing from a `:` exclusion term in the query.

### Result Type Trigger

Under **Settings → General → Quick Search Window → Result Type Priority**, you can configure a single-character **trigger** for specific result types (Applications, Settings, File Categories, Plugins, Files, etc.).

Typing the trigger as the very first character in the quick search window displays only that result type, hiding all others:

```text
;vs
```

If `;` is assigned to "Applications", the above query searches Visual Studio exclusively among applications. The trigger must be the first character with nothing before it, and it applies to the Quick and Inline search windows only. In both, History and Favorites remain pinned at the top regardless of triggers.

> [!NOTE]
> The trigger must be the first character of the query — a plugin token or sort token before it (`\img ;vs`) leaves the trigger unread, since the query no longer starts with it.

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
- **File Filters** (see [**Instant Answers**](./instant-answers#_5-file-filters)): bind a trigger keyword to chosen folders, then typing `keyword term` in the quick search window restricts a normal index search to those folders.

If you want to trigger custom scripts or launch programs using custom keywords, see [**Custom Commands**](./instant-answers#_6-custom-commands).
