using Lertaro.App.Helpers;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.App.ViewModels.Search;

/// <summary>
/// Which filter a row belongs to. One row belongs to at most one category; a row that belongs to none
/// (a source file, a plugin's own instant answer) is shown only under <see cref="SearchCategoryFilter.All"/>.
/// </summary>
/// <remarks>
/// The extension answer lives in <see cref="SearchCategoryClassifier"/>, which the engine's row filter and
/// the Core Extensions plugin's 自定义过滤器 rules also read -- the three surfaces cannot disagree about what
/// counts as a document, because there is only one table to say so. What is added here is what only an
/// <see cref="AppSearchResult"/> can answer: which provider contributed the row, whether it reports itself
/// as an application, and where a shortcut points.
/// </remarks>
public static class SearchResultCategorizer
{
    /// <summary>
    /// The filter a row belongs to, or null when it belongs to none.
    /// </summary>
    public static SearchCategoryFilter? Categorize(AppSearchResult result)
    {
        // Every entry the system-settings provider contributes is a settings entry: Bluetooth, Device Manager,
        // user account control, the lot. They are searchable items whose path is the GodMode namespace rather
        // than a file, and they also report themselves as applications, so the provider they came from is the
        // only reliable way to tell them apart from real applications.
        if (result.SourceProvider is PluginSdk.Abstractions.Plugins.ISearchableItemProvider settingsProvider
            && SearchResultTypePriority.GetProviderTypeId(settingsProvider)
                .Contains("SystemSettings", StringComparison.OrdinalIgnoreCase))
            return SearchCategoryFilter.Settings;

        // A shortcut is classified by what it points at, whichever way the row got here. The app provider
        // marks the ones it indexed as applications, and the shell calls every .lnk launchable -- but a
        // shortcut to a document or to a folder is not an application. The resolver answers from its own
        // cache, so the target's kind costs nothing after the first look at that shortcut.
        if (result.FullPath.EndsWith(SearchCategoryClassifier.ShortcutExtension, StringComparison.OrdinalIgnoreCase))
        {
            var target = ShortcutTargetResolver.Resolve(result.FullPath);
            if (target != null)
                return SearchCategoryClassifier.Classify(target.Path, target.IsDirectory);
        }

        var byPath = SearchCategoryClassifier.Classify(result.FullPath, result.IsDir);
        if (byPath != null)
            return byPath;

        if (result.IsApplication)
            return SearchCategoryFilter.Application;

        // A plugin instant answer (a calculator result, a command) is not a file and has no extension to
        // classify; leaving it unclassified keeps it out of every filter but All, where it belongs.
        return null;
    }

    /// <summary>Whether a row survives the given filter. <see cref="SearchCategoryFilter.All"/> keeps everything.</summary>
    public static bool Matches(SearchCategoryFilter category, AppSearchResult result) =>
        category == SearchCategoryFilter.All || Categorize(result) == category;
}
