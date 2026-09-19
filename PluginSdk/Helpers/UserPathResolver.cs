namespace Lertaro.PluginSdk.Helpers;

/// <summary>
/// Resolves a path the user typed or configured -- a favorite, a custom folder, a plugin's own folder
/// list -- into the real path filesystem and shell APIs need, from the two conveniences every one of
/// those fields accepts: <c>"%VAR%"</c> environment references and Windows shell virtual folders
/// (<c>"shell:Downloads"</c>, <c>"::{CLSID}"</c>).
/// </summary>
/// <remarks>
/// Both halves used to be re-implemented at each call site, with slightly different trimming and
/// case rules each time. The one place that owns how they combine is here; callers that only need to
/// tell a virtual token apart from a filesystem path use <see cref="IsVirtualPath"/> and deliberately
/// do NOT resolve it (a shell folder's localized display name is only reachable through the virtual
/// token), which is why the two steps are separate methods rather than one.
/// </remarks>
public static class UserPathResolver
{
    /// <summary>
    /// Expands <c>"%VAR%"</c> references and trims surrounding whitespace. A blank input is returned
    /// as it came in (and never as null), so callers can keep testing the result for emptiness.
    /// </summary>
    public static string Expand(string? rawPath)
        => string.IsNullOrWhiteSpace(rawPath) ? (rawPath ?? string.Empty) : Environment.ExpandEnvironmentVariables(rawPath.Trim());

    /// <summary>True for a shell namespace token (a virtual folder, or a packaged app's identity) rather than a filesystem path.</summary>
    public static bool IsVirtualPath(string? path) => ShellPathHelper.IsVirtualShellPath(path);

    /// <summary>
    /// Expands, then resolves a virtual path to what the shell says it is: the physical folder behind it,
    /// or its canonical <c>::{CLSID}</c> spelling when it has no physical folder.
    /// </summary>
    /// <remarks>
    /// A virtual folder that lives only inside the shell namespace -- <c>shell:AppsFolder</c>, This PC, the
    /// Recycle Bin -- has no filesystem path to return, and used to come back as the token the caller
    /// happened to write. It now comes back as the item's canonical name instead, so all spellings of one
    /// folder become one string (comparable, dedupable, recognisable) rather than as many as callers
    /// invent. That answer is still virtual, so a caller's "still virtual" test behaves exactly as before.
    /// A token the shell cannot parse at all -- a typo, or a non-shell path -- is returned unchanged; test
    /// <see cref="IsVirtualPath"/> on the result rather than guessing from the input either way, and check
    /// for the folder itself before handing the result to a filesystem API.
    /// </remarks>
    /// <param name="virtualPathResolver">
    /// Test seam for the shell lookup (<see cref="ShellPathHelper.TryResolveVirtualPath"/>), which is
    /// COM-backed and cannot be driven from a unit test.
    /// </param>
    public static string Resolve(string? rawPath, Func<string, string>? virtualPathResolver = null)
    {
        var expanded = Expand(rawPath);
        return (virtualPathResolver ?? ShellPathHelper.TryResolveVirtualPath)(expanded);
    }
}
