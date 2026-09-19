using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.CoreExtensions.Providers.Indexing;

/// <summary>
/// Keeps the folders scanned for application entries and the folders watched for cache invalidation in
/// lockstep. Split from <see cref="StartMenuAppItemProvider"/> to keep that provider below the
/// repository's per-file limit; it has no state of its own.
/// </summary>
internal static class StartMenuAppFolderRoots
{
    /// <summary>
    /// The shipped value of the custom-folder list: Windows' own "all apps" folder, so a machine nobody has
    /// configured still searches the applications a Start Menu scan alone misses.
    /// </summary>
    /// <remarks>
    /// shell:AppsFolder is a virtual shell folder with no filesystem path: UserPathResolver returns the
    /// token unchanged and Directory.Exists is false for it, so <see cref="Merge"/> drops it as a directory
    /// to scan. That is deliberate -- handing a virtual token to the directory indexer could only fail --
    /// and it costs nothing, because the provider's own shell enumeration of that folder is what returns
    /// those entries. This default therefore declares "applications are searched out of the box" rather
    /// than adding a folder to walk.
    /// </remarks>
    internal static readonly IReadOnlyList<string> DefaultCustomFolders = ["shell:appsfolder"];

    /// <summary>
    /// The custom folders to scan: the configured ones when there are any,
    /// <see cref="DefaultCustomFolders"/> otherwise.
    /// </summary>
    /// <remarks>
    /// Pure, so the rule is pinned by a test rather than by a settings store. A list with anything in it is
    /// the user's own and is used exactly as given -- nothing is appended to it, so configuring one folder
    /// does not silently re-add the shipped default beside it. An unset or empty one means "never
    /// configured" and takes the default. A blank entry does not count as configured: the field is one path
    /// per line, so a stray empty line must not be the thing that keeps the default out.
    /// </remarks>
    internal static IReadOnlyList<string> ResolveCustomFolders(List<string>? configured)
        => configured is { Count: > 0 } folders && folders.Any(folder => !string.IsNullOrWhiteSpace(folder))
            ? folders
            : DefaultCustomFolders;

    internal static IReadOnlyList<string> Merge(
        IEnumerable<string> builtInRoots,
        IEnumerable<string>? customRoots,
        Func<string, bool> directoryExists,
        Func<string, string>? resolveVirtualPath = null)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // The resolver owns both steps (expand "%VAR%", then resolve "shell:..."), so the injected one
        // takes the raw candidate too -- the tests' fake is a plain string substitution.
        var resolve = resolveVirtualPath ?? (path => UserPathResolver.Resolve(path));
        Add(builtInRoots);
        if (customRoots != null)
            Add(customRoots);
        return roots.ToList();

        void Add(IEnumerable<string> candidates)
        {
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                var path = resolve(candidate);
                if (!directoryExists(path))
                    continue;

                roots.Add(path);
            }
        }
    }
}
