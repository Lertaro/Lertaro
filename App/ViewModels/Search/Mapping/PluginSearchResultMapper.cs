using System.IO;
using System.Runtime.InteropServices;
using Lertaro.Core;
using Lertaro.App.Services;

using Lertaro.App.Services.Plugin;
using Lertaro.App.Services.ShellIcons;
namespace Lertaro.App.ViewModels.Search.Mapping;

public static class PluginSearchResultMapper
{
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    // Converts a provider's raw GDI HBITMAP into a frozen, thread-safe BitmapSource and immediately
    // releases the GDI handle -- mirrors SearchableItemCache.MaterializeIcon's own "caller must
    // DeleteObject" contract. Unlike that cache (which materializes once for a slow-changing static
    // catalog), this runs on every AddInstantResults call, since InstantResultItem's bitmap is
    // whatever the provider decided to hand over THIS keystroke (e.g. a live window thumbnail).
    private static System.Windows.Media.ImageSource? MaterializeHBitmapIcon(IntPtr hBitmap)
    {
        try
        {
            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchResultMapper] Failed to materialize HBitmapIcon for instant result: {ex.Message}", LogLevel.Error);
            return null;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    public static void AddInstantResults(List<AppSearchResult> uiResults, string query, string? highlightQuery, bool isInlineWindow)
    {
        if (isInlineWindow)
            return;

        // highlightQuery defaults to `query` for callers that already pass the clean (token-stripped)
        // keyword as `query` itself -- only BuildQuickResults needs the two to differ, since it passes
        // the untouched raw text (keyword + any " :xxx" token suffix) as `query` so instant-result
        // plugins (a calculator, unit converter, ...) can see the suffix, but that raw text is NOT
        // what should drive TextHighlighter -- the "::" token syntax isn't file-search operator syntax
        // FzfPattern knows how to strip, so it would survive into the highlight mask as literal
        // garbage and light up nothing (or something misleading).
        var effectiveHighlightQuery = highlightQuery ?? query;

        foreach (var provider in PluginManager.Instance.InstantResultProviders)
        {
            try
            {
                var results = provider.GetInstantResults(query);
                if (results == null)
                    continue;

                foreach (var item in results)
                {
                    if (item == null)
                        continue;

                    // Detect if the result represents a real file or directory to unlock native thumbnail and correct path display
                    var isRealFile = false;
                    var isRealDir = false;
                    var targetPath = item.ActionArgument ?? string.Empty;

                    if (item.ActionType == "Execute" && !string.IsNullOrWhiteSpace(targetPath))
                    {
                        // WSL targets stay opaque until the user explicitly opens them; probing here
                        // would wake the distro merely because a plugin result was displayed.
                        if (!WslPath.IsPath(targetPath))
                        {
                            if (File.Exists(targetPath)) isRealFile = true;
                            else if (Directory.Exists(targetPath)) isRealDir = true;
                        }
                    }

                    System.Windows.Media.ImageSource? iconOverride = null;
                    var iconPath = "";

                    if (item.HBitmapIcon != IntPtr.Zero)
                    {
                        // Takes priority over IconData -- a provider that hands over a real pre-loaded
                        // bitmap (e.g. WindowSwitcher's window-content thumbnail) means it, regardless
                        // of whatever static IconData/path fallback it also set for when no bitmap was
                        // ready yet.
                        iconOverride = MaterializeHBitmapIcon(item.HBitmapIcon);
                    }
                    else if (!string.IsNullOrWhiteSpace(item.IconData))
                    {
                        if (item.IconData.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
                        {
                            iconPath = item.IconData.Substring(5).Trim();
                        }
                        else
                        {
                            try
                            {
                                var color = string.IsNullOrWhiteSpace(item.IconColor) ? "DefaultPluginIconColor" : item.IconColor;
                                iconOverride = ShellIconHelper.CreateVectorIcon(item.IconData, color);
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"[SearchResultMapper] Failed to create vector icon for instant result: {ex.Message}", LogLevel.Error);
                            }
                        }
                    }
                    else if (!isRealFile && !isRealDir)
                    {
                        // Fallback vector icon only for custom textual action results
                        try
                        {
                            iconOverride = ShellIconHelper.CreateVectorIcon("M7 2v11h3v9l7-12h-4l3-8z", "DefaultPluginIconColor");
                        }
                        catch { }
                    }

                    var isVirtualPreview = targetPath.StartsWith("flow-preview:", StringComparison.OrdinalIgnoreCase)
                        || targetPath.StartsWith("__FLOW_PREVIEW__:", StringComparison.OrdinalIgnoreCase);
                    var resultKind = isRealFile ? "File" : (isRealDir ? "Directory" : "InstantResult");
                    var fullPath = (isRealFile || isRealDir || isVirtualPreview) ? targetPath : (!string.IsNullOrEmpty(iconPath) ? iconPath : $"__INSTANT_RESULT__:{provider.Name}:{item.Title}");
                    var parentDir = !string.IsNullOrWhiteSpace(item.Description)
                        ? item.Description
                        : ((isRealFile || isRealDir) ? Path.GetDirectoryName(targetPath) ?? string.Empty : string.Empty);

                    // If it is a real file, but ShellIconHelper has not cached it yet, request the shell icon dynamically
                    if ((isRealFile || isRealDir) && iconOverride == null)
                    {
                        iconOverride = ShellIconHelper.GetIconForPath(targetPath, isRealDir);
                    }

                    uiResults.Add(new AppSearchResult
                    {
                        Name = SanitizeSingleLine(item.Title),
                        FullPath = fullPath,
                        ParentDir = SanitizeSingleLine(parentDir),
                        IsDir = isRealDir,
                        Drive = string.Empty,
                        ResultKind = resultKind,
                        Index = uiResults.Count,
                        SearchQuery = effectiveHighlightQuery,
                        IconOverride = iconOverride,
                        InstantResultActionType = item.ActionType ?? "Copy",
                        InstantResultActionArgument = targetPath,
                        InstantResultOnExecute = item.OnExecute,
                        InstantResultOnExecuteFunc = item.OnExecuteFunc,
                        TabCompletion = SanitizeSingleLine(item.TabCompletion),
                        SourceProvider = provider
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchResultMapper] Error getting instant results from provider '{provider.Name}': {ex.Message}", LogLevel.Error);
            }
        }
    }

    public static bool AddPluginSearchActionResults(List<AppSearchResult> uiResults, string query, string? contextDirectory, bool isInlineWindow)
    {
        string? currentGroup = null;
        var added = false;
        var windowType = isInlineWindow ? PluginSdk.Abstractions.SearchWindowType.Inline : PluginSdk.Abstractions.SearchWindowType.Main;
        foreach (var match in PluginManager.Instance.SearchActionItems(query, windowType, contextDirectory))
        {
            var action = match.Registration.Action;
            var group = string.IsNullOrWhiteSpace(action.GroupName) ? TranslationManager.Instance["Action_DefaultGroup"] : action.GroupName;
            if (!string.Equals(currentGroup, group, StringComparison.Ordinal))
            {
                SearchResultMapper.AddSectionHeader(uiResults, group, query);
                currentGroup = group;
            }

            uiResults.Add(new AppSearchResult
            {
                Name = action.DisplayName,
                FullPath = $"__PLUGIN_ACTION__:{match.Registration.RuntimeActionId}",
                ParentDir = BuildPluginActionHint(match.Keyword, action.Parameters),
                ContextDirectory = contextDirectory ?? string.Empty,
                IsDir = false,
                Drive = string.Empty,
                ResultKind = "PluginAction",
                Index = uiResults.Count,
                SearchQuery = query,
                PluginActionId = match.Registration.RuntimeActionId,
                PluginActionArgumentText = match.ArgumentText,
                IconOverride = action.Icon,
                SourceProvider = match.Registration.Plugin
            });
            added = true;
        }

        return added;
    }

    public static string BuildPluginActionHint(string keyword, IReadOnlyList<string> parameters)
    {
        if (parameters.Count == 0)
            return string.Format(TranslationManager.Instance["Search_KeywordOnly"], keyword);

        return string.Format(TranslationManager.Instance["Search_KeywordParams"], keyword, string.Join(" ", parameters));
    }

    private static readonly char[] NewlineChars = ['\r', '\n'];

    internal static string SanitizeSingleLine(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (text.IndexOfAny(NewlineChars) < 0)
            return text;

        return text
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }
}
