using System.Windows;
using Lertaro.Core;

using Lertaro.App.ViewModels.Search.Mapping;

using SearchWindowType = Lertaro.PluginSdk.Abstractions.SearchWindowType;
namespace Lertaro.App.ViewModels.Search.Dispatch;

// Handles the quick-window-only "per-type search trigger character" feature on behalf of
// SearchDispatchController -- extracted into its own class (composition, not a partial class)
// purely to keep SearchDispatchController.cs under the repo's per-file line limit.
internal sealed class ResultTypeTriggerHandler
{
    private readonly Func<bool> _getIsInlineSearchContext;
    private readonly Action<bool> _setIsSearching;
    private readonly Action<Visibility> _setResultsPanelVisibility;
    private readonly Action<Visibility> _setResultsSeparatorVisibility;
    private readonly Action<IEnumerable<AppSearchResult>> _replaceResults;

    public ResultTypeTriggerHandler(
        Func<bool> getIsInlineSearchContext,
        Action<bool> setIsSearching,
        Action<Visibility> setResultsPanelVisibility,
        Action<Visibility> setResultsSeparatorVisibility,
        Action<IEnumerable<AppSearchResult>> replaceResults)
    {
        _getIsInlineSearchContext = getIsInlineSearchContext;
        _setIsSearching = setIsSearching;
        _setResultsPanelVisibility = setResultsPanelVisibility;
        _setResultsSeparatorVisibility = setResultsSeparatorVisibility;
        _replaceResults = replaceResults;
    }

    // Quick-window-only (unlike "*"/exclusion-bypass and token syntax, which are general search
    // syntax and apply everywhere): if `raw`'s first character matches a configured per-type trigger
    // (UserSettings.ResultTypeTriggers), strip it from cleanQuery before it's sent to the file-index
    // engine (RunEngineSearch's engineCall) and reaches BuildQuickResults -- so a "Files" trigger gets
    // the same clean-text recall from the backend as every other type gets locally, instead of the
    // backend matching against the trigger-polluted text. BuildQuickResults still independently
    // resolves WHICH type was triggered from originalValue/rawQuery (see
    // SearchResultTypePriority.ResolveTrigger) -- this only fixes what text gets searched with. The
    // returned type-id (null when no trigger matched) lets the caller show a type-specific prompt
    // instead of the generic "no results" row when stripping leaves nothing behind. The inline window
    // shares this same DispatchSearch/PerformSearch code path (see _getIsInlineSearchContext elsewhere
    // in this class), but has no concept of a per-type trigger at all -- BuildQuickResults' own
    // detection already skips it for isInlineWindow, so this must too, or an inline query that
    // happens to start with someone's configured trigger character would silently search the wrong
    // (one-character-short) text. A configured trigger WORD outranks this whole mechanism when the typed
    // first token is one -- see the ClaimsLeadingWord guard below, which is where that precedence lives.
    public (string CleanQuery, string? TriggeredTypeId) StripTrigger(string raw, string cleanQuery) =>
        Match(
            raw,
            cleanQuery,
            _getIsInlineSearchContext(),
            UserSettings.Load().ResultTypeTriggers ?? new Dictionary<string, string>(),
            // The inventory consulted for the word-beats-character precedence is the quick window's
            // (SearchWindowType.Main -- the value PluginSearchResultMapper hands SearchActionItems for this
            // same window). The inline window has already returned above.
            query => PluginTriggerQuery.ClaimsLeadingWord(query, SearchWindowType.Main));

    /// <summary>
    /// The decision itself, pure: given the two query strings, the window, the configured per-type triggers
    /// and a way to ask whether a registered WORD owns the token, which character (if any) comes off the
    /// text and which type it selects. Kept free of UserSettings and PluginManager so a test can pin every
    /// branch -- the precedence between a configured character and a configured word is the part that used
    /// to be wrong and the part a running window cannot show.
    /// </summary>
    internal static (string CleanQuery, string? TriggeredTypeId) Match(
        string raw,
        string cleanQuery,
        bool isInlineSearchContext,
        IReadOnlyDictionary<string, string> triggers,
        Func<string, bool> claimsLeadingWord)
    {
        if (isInlineSearchContext || raw.Length == 0 || cleanQuery.Length == 0 || cleanQuery[0] != raw[0])
            return (cleanQuery, null);

        var typeId = SearchResultTypePriority.ResolveTrigger(raw[0], triggers);
        if (typeId == null)
            return (cleanQuery, null);

        // A character trigger only ever inspects one character, so it would cut "set 路径" down to
        // "et 路径" the moment anyone assigns "s" to a type: the file search runs on the mangled text and
        // the word's own owner still recognises the raw input, so the user gets the provider's rows beside
        // garbage. A typed word that matches a registered trigger word outright wins over the character.
        // Consulted only once a character actually matches, so the common case (no type trigger at all,
        // which is the shipped default) pays nothing for this.
        if (claimsLeadingWord(cleanQuery))
            return (cleanQuery, null);

        return (cleanQuery.Substring(1), typeId);
    }

    // Same "nothing to search yet" situation as SearchDispatchController.ClearForTokenOnlyQuery, but for
    // a per-type trigger typed with no content after it -- "No Search Results" would be misleading here
    // since no search actually ran at all, so this names the type instead ("Keep typing to search
    // Applications only") to make clear what's being waited on.
    public void ShowPrompt(string typeId)
    {
        _setIsSearching(false);
        var typeName = SearchResultTypePriority.GetDisplayName(typeId) ?? string.Empty;
        _replaceResults(new[] { SearchResultMapper.CreateResultTypeTriggerPromptResult(typeName) });
        _setResultsPanelVisibility(Visibility.Visible);
        _setResultsSeparatorVisibility(Visibility.Visible);
    }
}
