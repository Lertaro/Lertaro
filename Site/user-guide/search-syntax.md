# Search Syntax

Lertaro's query box supports more than plain typing. Every operator below can be combined with
plain fuzzy terms in the same query.

## Fuzzy matching (default)

Type any part of a name and Lertaro finds it as long as the characters occur in order, anywhere
in the file/folder name — you don't need to type a contiguous substring:

| You type | Matches |
|---|---|
| `swlst` | `Lertaro.exe` |
| `report` | `Q3-report-final.docx` |

Turn this off under **Settings → General → System → Enable fuzzy matching** and a bare term
(no operator) instead has to appear as a contiguous substring — `abc` no longer matches `a-b-c`.
Every operator in the table below keeps working exactly the same either way; the setting only
changes what a bare term requires. The `'` operator flips exactness for one term regardless of the
setting, so you can drop a fuzzy word into an otherwise exact query, or an exact word into an
otherwise fuzzy one, without changing the setting itself.

## Multiple words

Separate words with a space. Each word narrows the result set further — it does **not** require
the words to appear in the same order you typed them:

```
report final
```

matches `final-Q3-report.docx` just as well as `Q3-report-final.docx`.

## Case sensitivity

- An **all-lowercase** query is case-insensitive: `myfile` matches `MyFile`, `MYFILE`, etc.
- A query with **any uppercase letter** becomes case-sensitive for that term: `MyFile` only
  matches `MyFile`, not `myfile`.

## Operators

| Prefix/Suffix | Example | Effect |
|---|---|---|
| *(none)* | `report` | Fuzzy match anywhere in the name (default). |
| `!` | `!temp` | **Exclude** results whose name contains the exact substring `temp` (this one is not fuzzy). |
| `'` | `'report` | **Flips exactness** for this one term — exact substring instead of fuzzy while fuzzy matching is on (the default); back to fuzzy for this term while fuzzy matching is turned off in Settings. |
| `'...'` | `'final report'` | Exact match anchored to word boundaries (won't match inside a larger word). |
| `^` | `^IMG` | **Prefix** match — the name must start with `IMG`. |
| `$` | `.pdf$` | **Suffix** match — the name must end with `.pdf`. |
| `^...$` | `^readme.md$` | **Equals** — the whole name must be exactly `readme.md`. Only when both wrap the *same* word; on separate words they stay independent prefix and suffix filters. |
| `\|` | `report \| summary` | **OR** — match either side of the pipe. |

You can mix these freely, e.g. `^IMG !.png$ 2024` finds files starting with `IMG`, from 2024,
that are *not* PNGs.

For an OR query, every term that actually matches a given result is highlighted in its name — not
just whichever term happened to match first — so `report | summary` highlights both words in a
result whose name contains them both.

## Pasting multiple lines

Paste text containing several lines — e.g. filenames copied one per line from a spreadsheet or text
file — and Lertaro automatically folds them into an OR query instead of pasting them as-is:

```
123
456
678
```

pastes as `123 | 456 | 678`, matching any of the three. Blank lines are skipped. A normal single-line
paste is unaffected.

## Targeting a drive

Start the query with a drive letter followed by a colon to restrict results to that drive, then
continue typing your search as normal:

```
d: report
```

searches only on the `D:` drive.

The space is optional: `d:report` means the same thing as `d: report`.

## Path mode

If your query contains a path separator (`\` or `/`), Lertaro switches to path mode and matches
against full paths instead of just names — useful for jumping straight to a known folder:

```
D:\Projects\Lertaro
```

A trailing separator (`D:\Projects\`) searches the *contents* of that exact folder.

## Filtering by folder name and wildcards (Query Tokens)

Lertaro supports chaining query tokens after your primary search keywords (prefixed by `:` by default or by dedicated token prefixes) to perform secondary filtering on primary search results:

- **Wildcard Secondary Filter (`:?<wildcard-expression>` or `?<wildcard-expression>`)**: Uses standard Windows wildcard syntax (`?` for any single character, `*` for zero or more characters) to filter primary search results. For example, `mp4 :?(2026???????????)` or `mp4 ?(2026???????????)` filters files with 2026 and 11-digit timestamp tags. Use `|` or `;` to specify multiple OR wildcard conditions (e.g., `?(2026*)|*.png`).
- **Path Match (`::<path-expression>`)**: Requires result name or ancestor folder to match the specified text (e.g. `1080 ::wallpapers` or `report ::2024`).
- **Custom Filter Categories (`:@<keyword>`)**: Applies pre-configured file extension or category rules (e.g. `:@doc` or `:@video`).

## When a term describes the folder, not the file

If matching by file and folder names alone doesn't fill the results, Lertaro tops them up by
additionally letting terms match ancestor folders — no special syntax needed:

```
d01j dcj
```

finds a file named `d01j` that lives in a folder named (or aliased to) `dcj`, even though `dcj`
never appears in the file's own name. This only fills in the rest of a query from the folders above
a file — at least one term still has to match the file name itself, and it only runs when an
ordinary name-only search has not filled the page. What it finds is appended after those results
rather than mixed into them, so it can never displace or reorder a result an ordinary search would
already have found. Ancestor folders are matched the same way file names are, so pinyin reaches a
Chinese folder name here too.

## Bypassing exclusion rules for one search

Start a query with `*` to search past your own [exclusion rules](./settings/index-drives#exclusion-rules) —
`ExcludedPaths`, ignored globs, and ignored regexes — just for that search, without changing your
settings:

```
*node_modules
```

The `*` itself is stripped before matching, so it's never treated as part of the search text. This
only reveals results that are already indexed; a folder that was *never* indexed in the first place
(an excluded folder on a network or WSL drive) still won't appear. Hidden/system files stay filtered
either way — this only affects your own exclusion-rule configuration. Typing just `*` with nothing
after it yet shows a "keep typing to search" prompt rather than "No Search Results", since no search
has actually run yet.

## Result type trigger

Optional, and off by default — you assign the character yourself. If you've assigned a trigger
character to a result type under **Settings → General → Quick Search Window → Result Type
Priority**, typing that character as the very first thing in the quick window shows only that
type's results — Applications, Settings, one specific File Filter, a plugin's own items, or plain
Files — hiding every other type:

```
;vs
```

finds "Visual Studio" among Applications only, if `;` is that type's configured trigger, regardless
of which other type's results would otherwise have matched the text better. Typing just the trigger
character with nothing after it yet shows a prompt naming the type instead of "No Search Results".
In Quick and inline windows, History and Favorites are unaffected either way — they always come first,
trigger or not. The full window does not add those recall entries to its results. No trigger
is configured by default; see [General settings](./settings/general#quick-search-window) to set one up.

## Chinese filenames: pinyin aliasing

Filenames containing Chinese characters are automatically searchable by pinyin, with no setup
required:

- **Full pinyin**: typing `chongqing` matches a file named `重庆`.
- **Initials**: typing `cq` also matches `重庆` (first letter of each syllable).
- **Polyphonic characters** (characters with more than one valid pronunciation) generate aliases
  for each common reading, so whichever pronunciation you think of is likely to match.

This is handled by a bundled alias plugin — see **Settings → Plugins** if you ever want to check
it's enabled.

## Spanish filenames: accent aliasing

Filenames containing Spanish accented characters (`á`, `é`, `í`, `ó`, `ú`, `ü`, `ñ`) are automatically searchable using plain ASCII letters, with no setup required:

- **Unaccented ASCII**: typing `cancion` matches `Canción.mp3`, `nino` matches `Niño.txt`, and `ciguena` matches `Cigüeña.png`.
- **Full highlighting**: matching characters (including accented characters in the original name) are highlighted accurately.

This is handled by the bundled `SpanishAlias` plugin — see **Settings → Plugins** if you want to verify it is enabled.

## Favorites, not custom aliases

Lertaro does not have a general-purpose "define your own alias/macro" system. The closest
equivalent is [Favorites](./settings/favorites): pin a folder, file, or URL under a custom display
name, and it becomes searchable by that name (shown with a ★ marker in results). If what you
actually want is a custom keyword that launches a program, see
[Custom Commands](./instant-answers#custom-commands) instead.
