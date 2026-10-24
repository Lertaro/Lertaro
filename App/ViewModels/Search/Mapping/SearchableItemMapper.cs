using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.App.Services;
using Lertaro.Core;

using Lertaro.App.Services.Plugin;
using Lertaro.App.Services.ShellIcons;
using Lertaro.Core.SearchIndex;
namespace Lertaro.App.ViewModels.Search.Mapping;

public static class SearchableItemMapper
{
    internal readonly record struct ShadowAliasCandidate(
        AppSearchResult Result,
        MatchRank Match,
        MatchEvidence Evidence,
        ISearchableItemProvider Provider);

    public static void Preload() => SearchableItemCache.Preload();

    // A learned Application history entry retains only its launched path. Reuse the cached provider
    // entry when it is still available so history results keep the application's own title, icon, and
    // execution delegate instead of reconstructing an almost-identical row from the shortcut path.
    internal static bool TryCreateApplicationResult(string path, string query, out AppSearchResult result)
    {
        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            SearchableItemCache.EnsureLoaded(provider);
            if (!SearchableItemCache.TryGetEntries(provider.GetType().Name, out var entries))
                continue;

            var entry = entries.FirstOrDefault(entry =>
                string.Equals(entry.Item.ResultKind, "Application", StringComparison.Ordinal) &&
                string.Equals(entry.Item.ActionArgument?.Trim(), path.Trim(), StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                continue;

            result = BuildCandidate(entry, provider, query, MatchRank.NoMatch).Result;
            return true;
        }

        result = null!;
        return false;
    }

    // Providers load on a background thread and a query issued before a given provider finishes is
    // silently missing its items (see CollectSearchableItemResults' cache-miss "continue" below) -- there is
    // no synchronous "wait for everything" alternative without blocking the UI. Instead, a live search
    // re-runs itself once more providers become available, so results stream in rather than staying
    // incomplete for the rest of the session. Raised on a background thread; subscribers must marshal
    // back to the UI thread themselves.
    public static event Action? ProviderLoaded
    {
        add => SearchableItemCache.ProviderLoaded += value;
        remove => SearchableItemCache.ProviderLoaded -= value;
    }

    // Returns candidates (with their ranking MatchRank) instead of appending directly to a results list --
    // the caller (SearchResultMapper.BuildQuickResults) merges these into one globally sorted list
    // alongside favorites/history-matched files/file-search results, rather than always showing every
    // searchable item ahead of every file result regardless of which actually matched better.
    internal static List<(AppSearchResult Result, MatchRank Match, MatchEvidence Evidence)> CollectSearchableItemResults(
        string query,
        bool isInlineWindow,
        List<ShadowAliasCandidate>? shadowAliases = null)
    {
        var candidates = new List<(AppSearchResult Result, MatchRank Match, MatchEvidence Evidence)>();
        if (isInlineWindow) return candidates;

        var q = query?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(q)) return candidates;

        // Parsed ONCE for the whole catalog scan. The per-entry string API re-parsed the query for every
        // title and every alias (2+ parses per text, and Parse re-runs each alias provider's
        // GetQueryForms), which dominated this loop's cost on a single keystroke.
        var fuzzy = FuzzyQuery.Parse(q);
        var queryWords = q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // Every matched entry -- across ALL providers, not just within one -- gets ranked by the same
        // match rank the file search hot path uses (FuzzyMatcher.ComputeBestMatch, against the entry's
        // own title -- same text TextHighlighter shows), instead of a fixed match-kind bucket order capped
        // PER PROVIDER.
        var matched = new List<(SearchableItemCache.CacheEntry Entry, MatchRank Match, MatchEvidence Evidence, string? MatchedSecondaryText, ISearchableItemProvider Provider, string ActiveQuery)>();

        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            SearchableItemCache.EnsureLoaded(provider);

            if (!SearchableItemCache.TryGetEntries(provider.GetType().Name, out var entries))
                continue;

            foreach (var entry in entries)
            {
                var providerId = provider.GetType().Name;
                var match = MatchCatalogEntryWithProvenance(fuzzy, queryWords, entry, providerId);
                if (match.Result.Rank.IsMatch)
                {
                    matched.Add((entry, match.Result.Rank, match.Result.Evidence, match.MatchedSecondaryText, provider, q));
                    continue;
                }

                // Scattered secondary-alias matches stay excluded from the active candidate set (#440),
                // while still observable on the shadow diagnostic channel when tracing is enabled.
                if (shadowAliases != null)
                {
                    var shadow = MatchShadowAliases(fuzzy, entry, providerId);
                    if (shadow.Rank.IsMatch)
                    {
                        var built = BuildCandidate(entry, provider, q, shadow.Rank);
                        shadowAliases.Add(new ShadowAliasCandidate(
                            built.Result, built.Match, shadow.Evidence, provider));
                    }
                }
            }
        }

        var matches = matched.OrderBy(m => m.Match, MatchRankQualityComparer.Instance);
        foreach (var (entry, match, evidence, matchedSecondaryText, provider, activeQuery) in matches)
        {
            var built = BuildCandidate(entry, provider, activeQuery, match, evidence.Field, matchedSecondaryText);
            candidates.Add((built.Result, built.Match, evidence));
        }

        return candidates;
    }

    internal static MatchRank MatchCatalogEntry(FuzzyQuery query, string[] queryWords, SearchableItemCache.CacheEntry entry) =>
        MatchCatalogEntryWithEvidence(query, queryWords, entry, null).Rank;

    internal static MatchEvidenceResult MatchCatalogEntryWithEvidence(
        FuzzyQuery query,
        string[] queryWords,
        SearchableItemCache.CacheEntry entry,
        string? providerId) =>
        MatchCatalogEntryWithProvenance(query, queryWords, entry, providerId).Result;

    internal static (MatchEvidenceResult Result, string? MatchedSecondaryText) MatchCatalogEntryWithProvenance(
        FuzzyQuery query,
        string[] queryWords,
        SearchableItemCache.CacheEntry entry,
        string? providerId)
    {
        var primary = query.RankWithEvidence(entry.Item.Title, MatchEvidenceField.DisplayTitle, providerId);
        // A strong title match -- including a pinyin reading -- is the strongest evidence this item can
        // offer. Secondary aliases may have a higher coverage score on a longer string (for example
        // "guohe" covers 5/9 of "GuoheView" but 2/4 of "果核看图"), and comparing only coverage let that
        // alias overwrite the title match, demoting a direct hit to SecondaryField. Aliases still compete
        // when the title only matched weakly or not at all.
        if (primary.Rank.IsStrong)
            return (primary, null);

        var best = primary;
        string? matchedSecondary = null;

        foreach (var alias in entry.Aliases)
        {
            var candidate = query.RankWithEvidence(alias, MatchEvidenceField.ProviderAlias, providerId);
            if (!candidate.Rank.IsStrong)
                continue;
            if (FuzzyMatcher.IsStronger(candidate.Rank, best.Rank))
            {
                best = candidate;
                matchedSecondary = alias;
            }
        }

        foreach (var alias in entry.ProviderAliases)
        {
            var field = alias.Kind switch
            {
                SearchableItemAliasKind.ShortcutTarget => MatchEvidenceField.ShortcutTarget,
                SearchableItemAliasKind.ProductMetadata => MatchEvidenceField.ProductMetadata,
                _ => MatchEvidenceField.CuratedAlias,
            };
            var candidate = query.RankWithEvidence(alias.Text, field, providerId, MatchQueryVariant.ProviderAlias);
            // Scattered matches against secondary metadata/aliases produce #440 false positives ("zbl");
            // require a clean match before admitting non-title representations.
            if (!candidate.Rank.IsStrong)
                continue;
            if (FuzzyMatcher.IsStronger(candidate.Rank, best.Rank))
            {
                best = candidate;
                matchedSecondary = alias.Text;
            }
        }

        if (best.Rank.IsMatch)
            return (best, best.Evidence.Field == MatchEvidenceField.DisplayTitle ? null : matchedSecondary);

        // Match Windows Control Panel task links: every query token prefixes a title/keyword word.
        if (queryWords.Length > 0 && queryWords.All(token => entry.KeywordWords.Any(
                word => word.StartsWith(token, StringComparison.OrdinalIgnoreCase))))
        {
            // A declared keyword is a synonym for the settings/application row, not a scattered guess.
            // Prefer the shortest matching word so "env" scores against "envvar" rather than "environment".
            var matchedWords = queryWords.Select(token => entry.KeywordWords
                .Where(word => word.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                .OrderBy(word => word.Length)
                .First()).ToArray();
            var coverage = matchedWords.Select((word, i) => (double)queryWords[i].Length / word.Length).Average();
            var wholeWord = matchedWords.Select((word, i) => word.Length == queryWords[i].Length).All(match => match);
            var rank = new MatchRank(
                MatchRank.TierFull,
                0,
                coverage,
                1,
                wholeWord ? MatchRank.ShapeWholeWord : MatchRank.ShapeWordStart);
            // Owner decision 2026-10-24: a clean keyword-prefix hit on a settings/application row is
            // ExactIdentity, so the configured type order can place Settings ahead of files. A one- or
            // two-letter Latin prefix is too ambiguous to be an identity; it still matches, but only as
            // SecondaryField. Non-ASCII (Chinese) tokens carry enough information at one character.
            var clean = queryWords.Select((token, i) => token.Length == matchedWords[i].Length
                || token.Length >= 3
                || token.Any(character => character > 0x7F)).All(isClean => isClean);
            var evidence = new MatchEvidence(
                MatchEvidenceField.Keyword,
                MatchEvidenceMode.LegacyUnknown,
                MatchQueryVariant.Original,
                clean ? MatchEvidenceGrade.ExactIdentity : MatchEvidenceGrade.SecondaryField,
                AllPositiveTerms: true,
                DirectText: false,
                ProviderId: providerId);
            return (new MatchEvidenceResult(rank, evidence), string.Join(' ', matchedWords));
        }
        return (new MatchEvidenceResult(MatchRank.NoMatch, MatchEvidence.LegacyUnknown), null);
    }

    internal static MatchEvidenceResult MatchShadowAliases(
        FuzzyQuery query,
        SearchableItemCache.CacheEntry entry,
        string? providerId)
    {
        var best = new MatchEvidenceResult(MatchRank.NoMatch, MatchEvidence.LegacyUnknown);
        foreach (var alias in entry.ProviderAliases)
        {
            var field = alias.Kind switch
            {
                SearchableItemAliasKind.ShortcutTarget => MatchEvidenceField.ShortcutTarget,
                SearchableItemAliasKind.ProductMetadata => MatchEvidenceField.ProductMetadata,
                _ => MatchEvidenceField.CuratedAlias,
            };
            var candidate = query.RankWithEvidence(
                alias.Text,
                field,
                providerId,
                MatchQueryVariant.ProviderAlias);
            if (FuzzyMatcher.IsStronger(candidate.Rank, best.Rank))
                best = candidate;
        }
        return best;
    }

    internal static string FormatSecondaryProvenance(MatchEvidenceField field, string? matchedText)
    {
        if (string.IsNullOrWhiteSpace(matchedText)
            || field is MatchEvidenceField.LegacyUnknown or MatchEvidenceField.DisplayTitle or MatchEvidenceField.FileName)
        {
            return string.Empty;
        }

        var key = field switch
        {
            MatchEvidenceField.ShortcutTarget => "Search_TargetSourceFormat",
            MatchEvidenceField.ProductMetadata => "Search_MetadataSourceFormat",
            _ => "Search_AliasSourceFormat",
        };
        // The indexer hands back "[key]" for anything it has never loaded, which would advertise a missing
        // translation instead of the row's provenance, so ask whether the template actually exists.
        if (!TranslationManager.Instance.TryGet(key, out var template) || string.IsNullOrWhiteSpace(template))
            return matchedText;

        return string.Format(template, matchedText);
    }

    // No filesystem path exists for a provider row, so the row carries a synthetic one. Its provider
    // segment is the CLR type name, not the localized display name: index rules and exported traces must
    // not depend on the UI language. Nothing parses the segment -- every consumer only checks the
    // "__SEARCHABLE_ITEM__:" prefix.
    internal static string SyntheticPath(ISearchableItemProvider provider, string title) =>
        $"__SEARCHABLE_ITEM__:{provider.GetType().Name}:{title}";

    private static (AppSearchResult Result, MatchRank Match) BuildCandidate(
        SearchableItemCache.CacheEntry entry,
        ISearchableItemProvider provider,
        string activeQuery,
        MatchRank match,
        MatchEvidenceField evidenceField = MatchEvidenceField.DisplayTitle,
        string? matchedSecondaryText = null)
    {
        var item = entry.Item;
        System.Windows.Media.ImageSource? iconOverride = null;

        var isRealFile = false;
        var isRealDir = false;
        var isApplication = false;
        var rKind = item.ResultKind ?? string.Empty;

        if (rKind == "File")
        {
            isRealFile = true;
        }
        else if (rKind == "Directory")
        {
            isRealDir = true;
        }
        else if (rKind == "Application")
        {
            // Keep the app's real target path (a Start Menu .lnk, or a virtual shell:AppsFolder
            // token for packaged apps) instead of the generic "__SEARCHABLE_ITEM__:" placeholder,
            // so file actions (copy, locate in explorer, ...) have something to act on -- each
            // action's own CanExecute already handles a path that doesn't exist on disk.
            isApplication = true;
        }

        if (entry.Icon != null)
        {
            // Frozen bitmap materialized once at load time (see EnsureLoaded); reused as-is with
            // no per-keystroke rebuild and no leaked GDI handle.
            iconOverride = entry.Icon;
        }
        else if ((isRealFile || isRealDir || isApplication) && !string.IsNullOrWhiteSpace(item.ActionArgument))
        {
            // Deliberately leave IconOverride unset here -- AppSearchResult.Icon's own getter (FullPath/
            // IsDir are set below to item.ActionArgument/isRealDir, exactly what this needs) already
            // does a cache-only check first, then a background-thread ShellIconHelper extraction
            // (semaphore-limited, marshaled back once ready) the same way every regular file search
            // result already gets its icon. Calling ShellIconHelper.GetIconForPath eagerly here instead
            // would set IconOverride and short-circuit that whole lazy/async path, forcing every File
            // Filter/Application/Settings item through a SYNCHRONOUS extraction on the UI thread -- for
            // a video file on a network drive, that means Windows' shell thumbnail handler reading the
            // file over SMB to decode a frame, blocking the search results render until it returns.
        }
        else if (!string.IsNullOrWhiteSpace(item.IconData))
        {
            try
            {
                var color = string.IsNullOrWhiteSpace(item.IconColor) ? "DefaultPluginIconColor" : item.IconColor;
                iconOverride = ShellIconHelper.CreateVectorIcon(item.IconData, color);
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchableItemMapper] Failed to create vector icon: {ex.Message}", LogLevel.Error);
            }
        }
        else
        {
            try
            {
                iconOverride = ShellIconHelper.CreateVectorIcon("M7 2v11h3v9l7-12h-4l3-8z", "DefaultPluginIconColor");
            }
            catch { }
        }

        return (new AppSearchResult
        {
            Name = item.Title,
            FullPath = (isRealFile || isRealDir || isApplication)
                ? item.ActionArgument
                : SyntheticPath(provider, item.Title),
            // Direct-title applications show name-only; secondary-alias hits surface their matched
            // provenance ("Alias: ...", "Target: ...") so non-title matches explain themselves visually.
            ParentDir = item.ResultKind == "Application"
                ? FormatSecondaryProvenance(evidenceField, matchedSecondaryText)
                : item.Description,
            IsDir = isRealDir,
            Drive = string.Empty,
            ResultKind = isRealFile ? "File" : (isRealDir ? "Directory" : (isApplication ? "Application" : "InstantResult")),
            SearchQuery = activeQuery ?? string.Empty,
            IconOverride = iconOverride,
            InstantResultActionType = item.ActionType ?? "Copy",
            InstantResultActionArgument = item.ActionArgument ?? string.Empty,
            InstantResultOnExecute = item.OnExecute,
            InstantResultOnExecuteFunc = item.OnExecuteFunc,
            TabCompletion = item.TabCompletion,
            SourceProvider = provider
        }, match);
    }
}
