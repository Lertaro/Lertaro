using System.IO;
using Lertaro.Core;
using Lertaro.App.Services;
using Lertaro.App.Services.Plugin;

namespace Lertaro.App.ViewModels.Search;

// When the search box is empty in an inline-search context, and the currently active Explorer
// window is sitting on a folder we haven't already scoped to, suggest jumping there instead of
// showing nothing. Extracted out of SearchExecutionViewModel to keep that file under the repo's
// line-count limit.
internal static class ExplorerJumpSuggestionHelper
{
    public static AppSearchResult? TryBuildSuggestion(bool isInlineSearchContext, string? searchScope)
    {
        var tracker = InlineSearchManager.Instance.ExplorerTracker;
        var lastPath = tracker.LastActiveExplorerPath;
        var isDialog = tracker.IsActiveWindowDialog;
        var dirExists = !string.IsNullOrEmpty(lastPath) &&
                        (WslPath.IsPath(lastPath) || Directory.Exists(lastPath) ||
                         (lastPath.Length >= 3 && lastPath[1] == ':' && lastPath[2] == '\\' && char.IsLetter(lastPath[0])));

        var searchScopeTrimmed = searchScope?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var lastPathTrimmed = lastPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var isSamePath = string.Equals(searchScopeTrimmed, lastPathTrimmed, StringComparison.OrdinalIgnoreCase);

        Logger.Log($"[Diagnosis] SearchScope='{searchScope}', isDialog={isDialog}, lastPath='{lastPath}', dirExists={dirExists}, isSamePath={isSamePath}", LogLevel.Debug);

        if (!isInlineSearchContext || !isDialog || !dirExists || string.IsNullOrEmpty(lastPath) || (!string.IsNullOrEmpty(searchScope) && isSamePath))
            return null;

        string? targetName = null;
        var className = tracker.LastActiveExplorerClassName;
        var windowTitle = tracker.LastActiveExplorerWindowTitle;
        if (className != null && windowTitle != null)
        {
            foreach (var collector in PluginSdk.Registries.ActivePathCollectorRegistry.GetCollectors())
            {
                if (PluginPerformanceMonitor.Measure(collector, () => collector.CanHandle(className, windowTitle)))
                {
                    targetName = collector.TargetName;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(targetName))
            return null;

        return new AppSearchResult
        {
            Name = targetName,
            FullPath = lastPath,
            ParentDir = lastPath,
            IsDir = true,
            Drive = string.Empty,
            ResultKind = "JumpToExplorerPath",
            Index = 0,
            SearchQuery = string.Empty
        };
    }
}
