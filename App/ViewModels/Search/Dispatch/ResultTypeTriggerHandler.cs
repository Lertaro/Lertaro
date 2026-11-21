using System.Windows;
using Lertaro.Core;

using Lertaro.App.ViewModels.Search.Mapping;

using SearchWindowType = Lertaro.PluginSdk.Abstractions.SearchWindowType;
namespace Lertaro.App.ViewModels.Search.Dispatch;

// Handles result-type trigger characters for the quick search window.
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

    // In Quick Search, strip a configured leading type character before sending the query to the engine.
    // BuildQuickResults still reads the original query to choose the type. Return the type ID so an
    // otherwise empty query can show its prompt.
    //
    // Skip this for inline searches, which have no type triggers. A configured plugin trigger word takes
    // precedence over a type character.
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
    ///
    /// The FULL search window calls this too (with isInlineSearchContext: false and Main's word inventory),
    /// using only the stripped text: it re-runs what the user typed there or carried over from the quick
    /// window, so the trigger character must not be searched as literal text there either -- that is what made
    /// one visible query ("，环境") answer differently in the two windows. The type the character names is
    /// deliberately dropped: that window selects types through its sidebar.
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
