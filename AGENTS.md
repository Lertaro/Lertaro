# AI Agent Development and Collaboration Guidelines for C# Projects

## Ponytail, lazy senior dev mode

Prefer the smallest correct solution and avoid unnecessary code.

Before writing any code, stop at the first rung that holds:

1. Does this need to be built at all? (YAGNI)
2. Does the standard library already do this? Use it.
3. Does a native platform feature cover it? Use it.
4. Does an already-installed dependency solve it? Use it.
5. Can this be one line? Make it one line.
6. Only then: write the minimum code that works.

## Rules

- No abstractions that weren't explicitly requested.
- No new dependency if it can be avoided.
- No boilerplate nobody asked for.
- Deletion over addition. Boring over clever. Fewest files possible.
- Check whether a simpler existing feature satisfies a complex request.
- When standard-library options are equally simple, choose the one that handles edge cases correctly.
- Mark intentional simplifications with a `ponytail:` comment. If the shortcut has a known ceiling (global lock, O(n²) scan, naive heuristic), the comment names the ceiling and the upgrade path.

Preserve trust-boundary validation, data-loss prevention, security, accessibility, hardware calibration, and explicit requirements. Add MSTest coverage for non-trivial logic in the same change. See [DEVELOPMENT_GUIDE.md](DEVELOPMENT_GUIDE.md), rules 10, 11, and 14, for scope and structure. Trivial one-liners and pure UI/XAML glue need no test.

These rules also apply to repository tooling and guidelines, including this file.
