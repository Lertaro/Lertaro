namespace Lertaro.Core.IndexV2.Space;

/// <summary>A detached, wire-safe view of one entry in the current in-memory index.</summary>
public readonly record struct SpaceIndexEntry(
    string Path,
    string Name,
    long Size,
    bool IsDirectory,
    bool IsHardLinkDuplicate);

/// <summary>
/// Which children a space query wants. Applied inside the index walk, not after it: a row that is
/// filtered out here costs nothing, while a row that survives costs a full path rebuild, a recursive
/// size and a caller-permission open. Folder-size clients ask for a few hundred directories out of a
/// directory that holds thousands of rows.
/// </summary>
[Flags]
public enum SpaceEntryFilters : byte
{
    None = 0,
    DirectoriesOnly = 1,
    FilesOnly = 2,
    // The space view hides System-flagged children because a disk-usage browser has no use for them.
    // Everything-compatible answers must not: dropping them loses whole folders (and understates the
    // parent's total) for a caller that only wants sizes.
    IncludeSystemItems = 4,
    // Report rows the caller cannot open instead of vetting every one of them. The queried directory is
    // still really opened, and other users' profile roots are still excluded -- what goes away is the
    // per-row impersonated open, which cost ~60us each and silently dropped the SYSTEM-only folders a
    // folder-size client must have an answer for. The search surface already answers on that rule.
    SkipCallerRowAccessChecks = 8,
}
