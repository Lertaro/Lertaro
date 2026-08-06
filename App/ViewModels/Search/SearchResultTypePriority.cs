using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.Services.PluginManagerCore;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.ViewModels.Search;

// Resolves the "type" tier a BuildQuickResults candidate belongs to for the quick window's
// user-orderable result-type-priority feature (UserSettings.ResultTypeOrder) -- generalizes what used
// to be a single "boost applications" toggle into an id per ISearchableItemProvider (Applications,
// Settings, File Filters, any third-party plugin) plus one synthetic id for raw file-index results.
public static class SearchResultTypePriority
{
    // No ISearchableItemProvider sits behind the fileResults candidate loop in BuildQuickResults --
    // mirrors the "__builtin::..." synthetic-id convention QuickPanelSettings.TabOrder already uses
    // for its own non-plugin entries.
    public const string FilesTypeId = "__builtin::Files";

    public static string GetProviderTypeId(ISearchableItemProvider provider) =>
        PluginLoaderHelper.MakeId(ComponentFilter.GetDllName(provider), PluginComponentType.SearchableItemProvider, provider.GetType().Name);

    // Position in the user's saved order (most-preferred first); an id that isn't listed yet falls back
    // to int.MaxValue, which -- since the caller's sort is stable -- lands it after every listed type
    // while preserving its original relative order against any OTHER unlisted type, matching the same
    // fallback convention QuickNavigationProviderOrder/QuickPanel.TabOrder already use.
    public static int Rank(string typeId, List<string> order)
    {
        var idx = order.IndexOf(typeId);
        return idx >= 0 ? idx : int.MaxValue;
    }

    // The reverse lookup for UserSettings.ResultTypeTriggers -- a handful of entries at most, so a
    // linear scan beats maintaining a second reversed dictionary in sync.
    public static string? ResolveTrigger(char firstChar, IReadOnlyDictionary<string, string> triggers)
    {
        foreach (var (typeId, trigger) in triggers)
        {
            if (trigger.Length == 1 && trigger[0] == firstChar)
                return typeId;
        }
        return null;
    }

    // Same display text ResultTypeOrderViewModel shows for this id in Settings -- used by
    // SearchDispatchController to name the type in its "keep typing to search only X" prompt when a
    // trigger was typed with nothing after it yet.
    public static string? GetDisplayName(string typeId)
    {
        if (typeId == FilesTypeId)
            return TranslationManager.Instance["General_ResultTypeFiles"];

        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            if (GetProviderTypeId(provider) == typeId)
                return provider.Name;
        }
        return null;
    }

    // Used whenever the quick window's current query is carried over somewhere that has no concept
    // of a per-type trigger at all -- opening the full/main SearchWindow (Ctrl+F, or clicking "Show
    // more search results"). Without this, a query like ";vs" would land in the full window searching
    // literally for ";vs" instead of "vs".
    public static string StripLeadingTrigger(string query)
    {
        if (query.Length == 0)
            return query;
        return ResolveTrigger(query[0], UserSettings.Load().ResultTypeTriggers) != null
            ? query.Substring(1)
            : query;
    }
}
