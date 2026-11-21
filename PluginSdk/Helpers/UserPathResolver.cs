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
    /// Expands environment variables and resolves Shell tokens to physical folders or canonical ::{CLSID}
    /// names. Unresolvable tokens are returned unchanged. Canonical names for virtual-only folders remain
    /// virtual; check IsVirtualPath and folder existence before calling filesystem APIs. The optional
    /// virtualPathResolver supplies the Shell lookup for tests.
    /// </summary>
    public static string Resolve(string? rawPath, Func<string, string>? virtualPathResolver = null)
    {
        var expanded = Expand(rawPath);
        return (virtualPathResolver ?? ShellPathHelper.TryResolveVirtualPath)(expanded);
    }

    /// <summary>
    /// Resolves a path for navigation while preserving a virtual Shell token when no physical path exists.
    /// </summary>
    public static string ResolveForNavigation(string? rawPath, Func<string, string>? virtualPathResolver = null)
    {
        var expanded = Expand(rawPath);
        var resolved = (virtualPathResolver ?? ShellPathHelper.TryResolveVirtualPath)(expanded);
        return IsVirtualPath(resolved) ? expanded : resolved;
    }
}
