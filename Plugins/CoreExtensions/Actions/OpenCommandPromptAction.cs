using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.CoreExtensions.Actions;

public class OpenCommandPromptAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_GroupName_Cmd");

    public string DisplayName => TranslationService.Get("Action_OpenCmd");

    public string Description => TranslationService.Get("Action_OpenCmd_Desc");

    public IReadOnlyList<string> Keywords => new[] { "cmd" };

    public bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => windowType == SearchWindowType.Inline;

    public bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => results.Count == 1 && results[0].IsDir;

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M3 5h18v14H3V5zm2 2v10h14V7H5zm2 2 3 3-3 3V9zm5 6h5v-2h-5v2z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count == 1 && !string.IsNullOrWhiteSpace(results[0].ContextDirectory) && Directory.Exists(results[0].ContextDirectory);

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view) => CommandPromptLauncher.Open(results[0].FullPath, results[0].ContextDirectory, runAsAdmin: false);
}

public class OpenAdminCommandPromptAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_GroupName_Cmd");

    public string DisplayName => TranslationService.Get("Action_OpenAdminCmd");

    public string Description => TranslationService.Get("Action_OpenAdminCmd_Desc");

    public IReadOnlyList<string> Keywords => new[] { "cmda" };

    public bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => windowType == SearchWindowType.Inline;

    public bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => results.Count == 1 && results[0].IsDir;

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M3 5h18v14H3V5zm2 2v10h14V7H5zm2 2 3 3-3 3V9zm5 6h5v-2h-5v2z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count == 1 && !string.IsNullOrWhiteSpace(results[0].ContextDirectory) && Directory.Exists(results[0].ContextDirectory);

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view) => CommandPromptLauncher.Open(results[0].FullPath, results[0].ContextDirectory, runAsAdmin: true);
}

internal static class CommandPromptLauncher
{
    public static void Open(string pathText, string contextDirectory, bool runAsAdmin)
    {
        var workingDirectory = ResolveWorkingDirectory(pathText, contextDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/K cd /d \"{workingDirectory}\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        };

        if (runAsAdmin)
        {
            startInfo.Verb = "runas";
        }

        Process.Start(startInfo);
    }

    private static string ResolveWorkingDirectory(string pathText, string contextDirectory)
    {
        var path = (pathText ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path))
        {
            return ResolveFallbackDirectory(contextDirectory);
        }

        try
        {
            if (Directory.Exists(path))
            {
                return Path.GetFullPath(path);
            }

            if (File.Exists(path))
            {
                var parent = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    return parent;
                }
            }

            var fullPath = Path.GetFullPath(path);
            var directory = Path.HasExtension(fullPath) ? Path.GetDirectoryName(fullPath) : fullPath;
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return directory;
            }
        }
        catch
        {
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string ResolveFallbackDirectory(string contextDirectory)
    {
        var directory = (contextDirectory ?? string.Empty).Trim().Trim('"');
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            return Path.GetFullPath(directory);
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
