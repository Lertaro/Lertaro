using System.IO;
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

    // Directories from the same sources as Files (index, favorites, history, Windows Recent). Its own entry so
    // "Applications, Files, Folders" can be said in the order; until then folders shared the Files rank.
    public const string FoldersTypeId = "__builtin::Folders";

    // The ResultKind AppSearchResult.IsApplication reads. A named constant so the shortcut-identity path
    // below and the app provider's own declaration cannot drift apart.
    public const string ApplicationResultKind = "Application";

    // Rank 0..3 are the four built-in buckets the user asked to order explicitly (Applications, System
    // settings, Files, Folders). Anything beyond that -- a third-party provider, or a row whose type is not
    // listed -- is neutral in the rank kernel and must not be moved by the type order.
    public const int CoreResultTypeCount = 4;

    public static bool IsCoreTypeRank(int rank) => rank >= 0 && rank < CoreResultTypeCount;

    public static string GetProviderTypeId(ISearchableItemProvider provider) =>
        PluginLoaderHelper.MakeId(ComponentFilter.GetDllName(provider), PluginComponentType.SearchableItemProvider, provider.GetType().Name);

    // Position in the user's saved order (most-preferred first); an id that isn't listed yet falls back
    // to int.MaxValue, which -- since the caller's sort is stable -- lands it after every listed type
    // while preserving its original relative order against any OTHER unlisted type, matching the same
    // fallback convention QuickNavigationProviderOrder/QuickPanel.TabOrder already use.
    public static int Rank(string typeId, IReadOnlyDictionary<string, int> ranks) =>
        ranks.TryGetValue(typeId, out var rank) ? rank : int.MaxValue;

    /// <summary>First occurrence wins, matching IndexOf. Ordinal comparer keeps the old case sensitivity.</summary>
    public static Dictionary<string, int> BuildRanks(List<string> order)
    {
        order = WithFolders(order);
        var ranks = new Dictionary<string, int>(order.Count, StringComparer.Ordinal);
        for (var i = 0; i < order.Count; i++)
            ranks.TryAdd(order[i], i);
        return ranks;
    }

    public static int Rank(string typeId, List<string> order) => Rank(typeId, BuildRanks(order));

    // A saved order from before Folders existed lists Files but not Folders: Folders takes the slot right
    // after Files, so files lead folders at an equally good match. Read-time rather than a settings rewrite,
    // so every reader agrees and the Settings list shows the same slot it will save. An order that never
    // listed Files (nothing saved yet) is left alone: no type order applies there at all.
    public static List<string> WithFolders(List<string> order)
    {
        if (order.Contains(FoldersTypeId))
            return order;
        var files = order.IndexOf(FilesTypeId);
        if (files < 0)
            return order;
        var withFolders = new List<string>(order);
        withFolders.Insert(files + 1, FoldersTypeId);
        return withFolders;
    }

    // Whether the quick window caps an exact-named directory at DirectStrong (RankPolicy.GradeForRanking),
    // so that a type ranked above Folders -- an application, a setting -- can win against a folder with the
    // same leading word. Only Files above Folders does not count: the cap exists to make room for another
    // kind of thing, and files beat folders by the type order already. With nothing but file buckets on top,
    // the cap would only let a keyword-only ExactIdentity on a settings row outrank every exact folder.
    public static bool FoldersBehindAnotherType(List<string> order)
    {
        order = WithFolders(order);
        var folders = order.IndexOf(FoldersTypeId);
        if (folders < 0)
            return true;
        for (var i = 0; i < folders; i++)
        {
            if (order[i] != FilesTypeId)
                return true;
        }
        return false;
    }

    // The file bucket's rank when another built-in type sits above it, i.e. when the quick window caps an
    // exact file name so that type can win (RankPolicy.GradeForRanking). Null when Files leads the order or
    // is not one of the ordered built-in buckets at all: no type to make room for, or no rank to tell file
    // rows apart from unlisted ones. Folders above Files does not count, the mirror of FoldersBehindAnotherType:
    // folders already beat files by the type order, and the cap would only hand the win to a type ranked below.
    public static int? FilesRankBehindAnotherType(List<string> order)
    {
        order = WithFolders(order);
        var files = order.IndexOf(FilesTypeId);
        if (!IsCoreTypeRank(files))
            return null;
        for (var i = 0; i < files; i++)
        {
            if (order[i] != FoldersTypeId)
                return files;
        }
        return null;
    }

    // The type rank of the provider(s) declaring this ResultKind -- the best one when several do.
    // int.MaxValue when none of them declares it or the type is not in the saved order, i.e. the same
    // "unlisted" fallback every other lookup uses.
    public static int RankForResultKind(
        string resultKind,
        IEnumerable<ISearchableItemProvider> providers,
        IReadOnlyDictionary<string, int> ranks)
    {
        var rank = int.MaxValue;
        foreach (var provider in providers)
        {
            if (!string.Equals(provider.ItemResultKind, resultKind, StringComparison.Ordinal))
                continue;
            rank = Math.Min(rank, Rank(GetProviderTypeId(provider), ranks));
        }
        return rank;
    }

    // A shortcut must still look like an application when the Start Menu provider is not loaded: the live
    // provider is the only thing that declares ResultKind "Application", so without a fallback a .lnk stays
    // on the Files rank and cannot be moved by the configured order at all. Use the best listed type that is
    // not a file bucket as that fallback -- normally Applications itself, the first bucket in the default order.
    public static int RankForApplication(
        IEnumerable<ISearchableItemProvider> providers,
        IReadOnlyDictionary<string, int> ranks)
    {
        var rank = RankForResultKind(ApplicationResultKind, providers, ranks);
        if (rank != int.MaxValue)
            return rank;

        foreach (var (typeId, candidate) in ranks)
        {
            if (typeId is not (FilesTypeId or FoldersTypeId))
                rank = Math.Min(rank, candidate);
        }
        return rank;
    }

    // A shortcut file (.lnk/.appref-ms) is two things at once: a file-index row, and -- wherever the
    // app provider indexes it -- an application row. Ranking takes the better of the two ranks, so moving
    // "Applications" below "Files" in the order can never demote a shortcut: whichever identity the user
    // ranked higher is the one that counts. An .exe is deliberately NOT a shortcut here -- it is a file
    // the user searched for anywhere, and giving every build output the app bonus would invert that. A
    // .url is excluded for the same kind of reason: it opens a web page in a browser, so it is a file row
    // the user searches for, not an installed program.
    public static int ShortcutRank(int appRank, int filesRank) => Math.Min(appRank, filesRank);

    public static bool IsShortcutPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase);
    }

    // The per-row decision behind ShortcutRank: only a row that is still on the Files rank, is not a
    // directory, and is a shortcut file takes the better rank. Everything else -- including an .exe, and a
    // folder whose name merely ends in ".lnk" -- keeps the rank its type already gave it.
    public static int TypeRankForRow(int typeRank, bool isDirectory, string normalizedPath, int filesRank, int shortcutRank) =>
        typeRank == filesRank && !isDirectory && IsShortcutPath(normalizedPath) ? shortcutRank : typeRank;

    // The reverse lookup for UserSettings.ResultTypeTriggers -- a handful of entries at most, so a
    // linear scan beats maintaining a second reversed dictionary in sync. Compared case-insensitively,
    // like every other keyword comparison in the search box: a configured "A" has to fire for a typed "a",
    // or a trigger character would be the one rule silently depending on the Shift key.
    public static string? ResolveTrigger(char firstChar, IReadOnlyDictionary<string, string> triggers)
    {
        foreach (var (typeId, trigger) in triggers)
        {
            if (trigger is { Length: 1 } && char.ToUpperInvariant(trigger[0]) == char.ToUpperInvariant(firstChar))
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
        if (typeId == FoldersTypeId)
            return TranslationManager.Instance["General_ResultTypeFolders"];

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
        return StripLeadingTrigger(query, UserSettings.Load().ResultTypeTriggers);
    }

    // Same rule against a caller-supplied trigger table, for the paths that already have the settings
    // graph loaded or need the decision without touching it (see SearchResultExecutionHelper).
    public static string StripLeadingTrigger(string query, IReadOnlyDictionary<string, string> triggers)
    {
        if (query.Length == 0)
            return query;
        return ResolveTrigger(query[0], triggers) != null ? query.Substring(1) : query;
    }
}
