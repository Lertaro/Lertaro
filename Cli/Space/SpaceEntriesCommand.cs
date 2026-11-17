using Lertaro.Core.IndexV2.Space;

namespace Lertaro.Cli.Space;

/// <summary>Parses the non-interactive batch query used by external folder-size integrations.</summary>
public static class SpaceEntriesCommand
{
    public const string Switch = "--space-entries";
    public const string FolderSizesSwitch = "--space-folder-sizes";

    public static bool IsRequested(string[] args) => args.Length > 0 &&
        (string.Equals(args[0], Switch, StringComparison.OrdinalIgnoreCase)
         || string.Equals(args[0], FolderSizesSwitch, StringComparison.OrdinalIgnoreCase));

    public static bool TryGetDirectory(string[] args, out string directory)
    {
        directory = string.Empty;
        if (!IsRequested(args) || args.Length != 2)
            return false;

        directory = args[1];
        return true;
    }

    // Directory Opus only needs child-directory sizes. The parent itself is still opened under the
    // caller's identity, but opening every returned row as well makes a directory with tens of
    // thousands of children exceed the CLI's synchronous timeout. Those rows are already present in
    // Directory Opus's own view, so the per-row ACL probe adds delay without adding information.
    public static SpaceEntryFilters GetFilters(string[] args) =>
        args.Length > 0 && string.Equals(args[0], FolderSizesSwitch, StringComparison.OrdinalIgnoreCase)
            ? SpaceEntryFilters.DirectoriesOnly
              | SpaceEntryFilters.SkipCallerRowAccessChecks
              | SpaceEntryFilters.SkipPathMaterialization
              | SpaceEntryFilters.SkipSort
            : SpaceEntryFilters.None;

    public static bool IncludesPaths(string[] args) =>
        (GetFilters(args) & SpaceEntryFilters.SkipPathMaterialization) == 0;
}
