using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

/// <summary>
/// Offers to jump to a folder the user typed as a complete path: "D:\abc\d", "D:/agc/",
/// "%LOCALAPPDATA%\abc/d/e", "shell:Downloads\bbb\ccc", '\\server\share\x', with or without the whole
/// thing wrapped in quotes. Nothing is offered until the folder is there, so an unfinished or mistyped
/// path leaves the ordinary (full-path mode) search results untouched rather than answering with a
/// folder that cannot be opened.
/// </summary>
/// <remarks>
/// This is deliberately NOT a new operator and not part of <c>SearchQueryParser</c>'s path mode. Path mode
/// asks the index "which files live under something shaped like this", runs in the service process on a
/// lowercased copy of the text, and has no shell access -- so it can neither confirm that a "shell:" token
/// names an existing folder nor open one. The two answers coexist: this adds one row on top of the results
/// path mode already returns.
///
/// Relative paths are rejected on purpose. There is no anchor that would not surprise someone: the
/// process' own working directory is the one a bare "Core\SearchIndex" would resolve against, and it is
/// nowhere near what the user is looking at.
/// </remarks>
public class DirectoryJumpInstantProvider : IInstantResultProvider
{
    public string Name => TranslationService.Get("DirectoryJump_Name");

    // Folder glyph, as the other path-shaped rows in this folder draw it.
    private const string FolderIcon = "M20 6h-8l-2-2H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2z";

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (!TryResolve(query, out var folderPath))
            yield break;

        yield return new InstantResultItem
        {
            Title = folderPath,
            Description = TranslationService.Get("DirectoryJump_OpenHint"),
            IconData = FolderIcon,
            IconColor = "AccentBlue",
            ActionType = "Execute",
            ActionArgument = folderPath,
            TabCompletion = folderPath,
            // The host's own folder route: a new tab in an already-open Explorer window when there is one,
            // a new window otherwise, and whatever default file manager is configured. It has to be said
            // here rather than left to the row's path because a virtual or UNC target is one
            // PluginSearchResultMapper cannot confirm with File/Directory.Exists -- for those the row keeps
            // no path at all, and the shell would be handed the placeholder instead of the folder.
            OnExecuteFunc = () =>
            {
                ExplorerService.OpenFolder(folderPath);
                return true;
            }
        };
    }

    /// <summary>
    /// Whether this raw query names an existing folder, and which path to open.
    /// </summary>
    /// <remarks>
    /// The raw text is what gets read, before anything normalises it: the search box has already spent
    /// quotes and case on other things by then, and a path's own spelling is the information here.
    /// </remarks>
    internal static bool TryResolve(string? rawQuery, out string folderPath)
    {
        folderPath = string.Empty;

        var text = TrimSurroundingQuotes((rawQuery ?? string.Empty).Trim());
        if (text.Length == 0 || IsBareVariable(text))
            return false;

        var expanded = UserPathResolver.Expand(text);
        // A '%' that survived the expansion is an unknown variable, so this is a typo rather than a path.
        if (expanded.Contains('%', StringComparison.Ordinal))
            return false;

        // '/' is Windows' alternate separator, and the filesystem APIs resolve it themselves -- but a shell
        // token is parsed by the shell, which is not obliged to. Neither "shell:" nor a "::{CLSID}" can
        // contain a '/', so rewriting every one of them costs nothing and gives every consumer one spelling.
        var path = expanded.Replace('/', '\\');

        // ponytail: a folder on a network share whose server is gone blocks this on the SMB timeout, with
        // no deadline to give up. Only a complete path can get this far, so the exposure is the user's own
        // "\\server\..." typing, one probe per debounced keystroke.
        if (!IsCompletePath(path) || !PathAvailability.IsFolderAvailable(path))
            return false;

        folderPath = path;
        return true;
    }

    // A drive ("D:\..."), a share ("\\server\..."), or a shell namespace token ("shell:...", "::{CLSID}").
    // "D:" and "\abc" are deliberately absent: both are relative to something (a drive's current directory,
    // or the current drive) that only the process knows.
    internal static bool IsCompletePath(string path)
        => UserPathResolver.IsVirtualPath(path)
           || path.StartsWith(@"\\", StringComparison.Ordinal)
           || (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] == '\\');

    // One matching pair of surrounding quotes is how a path is written when it is pasted from elsewhere.
    // Quotes are ordinary characters to the search syntax (see QueryTokenScanner), so this is the only
    // place that reads them -- which is also why an unbalanced pair is left alone.
    private static string TrimSurroundingQuotes(string text)
        => text.Length >= 2 && (text[0] == '"' || text[0] == '\'') && text[^1] == text[0]
            ? text[1..^1].Trim()
            : text;

    /// <summary>Whether the query is nothing but one variable reference, e.g. "%TEMP%".</summary>
    /// <remarks>
    /// That one is <see cref="EnvironmentVariableInstantProvider"/>'s subject -- "what does this variable
    /// hold" -- while anything longer is a path being navigated to. The env provider defers to
    /// <see cref="TryResolve"/> rather than repeating that rule, so one query never earns two rows.
    /// </remarks>
    internal static bool IsBareVariable(string text)
        => text.Length > 2 && text[0] == '%' && text[^1] == '%' && !text.AsSpan(1, text.Length - 2).Contains('%');
}
