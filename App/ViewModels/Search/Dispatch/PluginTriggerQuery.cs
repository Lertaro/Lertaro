using Lertaro.Core;
using Lertaro.App.Services.Plugin;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.ViewModels.Search.Dispatch;

/// <summary>
/// Removes a leading trigger word from the text that gets searched against file and application names --
/// so "cs report" looks for files called "report" rather than fuzzy-matching "cs" and highlighting the
/// trigger inside every result row it dragged in.
/// </summary>
/// <remarks>
/// Two families of word, collected from live plugin state so the host never keeps a copy of either: an
/// instant provider's configured word (<see cref="IInstantResultProvider.QueryTriggerKeywords"/>, read per
/// call so a Settings change takes effect on the next keystroke) and a search action's own
/// <em>Keywords</em> ("mkdir", "touch", "cmd" -- the words KeywordMatcher already
/// treats as a command with an argument, which today also get matched as text and highlighted).
///
/// Same shape as <see cref="FileFilterScopeResolver"/>: a thin collector over PluginManager and a pure
/// <see cref="Match"/> holding the activation rules, so the rules are testable without the registry.
///
/// Providers still receive the untouched box text (see SearchExecutionEngine's instantQuery, and the raw
/// query the full window hands its content provider), because the owner of a word has to keep recognising
/// it; what this changes is only what the OTHER results are matched and highlighted against. The price,
/// paid deliberately: a first token that happens to be a command word stops being searchable as text --
/// which is why nothing is stripped unless a term follows it.
/// </remarks>
internal static class PluginTriggerQuery
{
    public static string Strip(string query)
    {
        var keywords = CollectKeywords();
        return Match(query, keywords, out var remainder) ? remainder : query;
    }

    private static IReadOnlyList<string> CollectKeywords()
    {
        var collected = new List<string>();

        // Instant providers: each reads its own configured word out of its plugin settings, and the host
        // only ever sees what the user actually set.
        foreach (var provider in PluginManager.Instance.InstantResultProviders)
        {
            IReadOnlyList<string>? declared;
            try
            {
                // Arbitrary plugin code, and this now runs on every keystroke: one provider throwing while
                // reading its own settings must not cost the user the whole search.
                declared = provider.QueryTriggerKeywords;
            }
            catch (Exception ex)
            {
                Logger.Log($"[PluginTriggerQuery] {provider.GetType().Name}.QueryTriggerKeywords failed: {ex.Message}", LogLevel.Error);
                continue;
            }

            if (declared == null) continue;
            foreach (var keyword in declared)
                if (!string.IsNullOrWhiteSpace(keyword))
                    collected.Add(keyword.Trim());
        }

        // Search actions (mkdir / touch / cmd ...): KeywordMatcher already treats "mkdir sub" as the action
        // with argument "sub", so the command word is a trigger by the same right -- and today the file list
        // beside it is matched and highlighted against "mkdir sub", which is neither what the argument means
        // nor anything the user wanted. Same cost to pay: a first token that happens to be an action word is
        // no longer searchable as text, so this only ever fires when a term follows the word.
        foreach (var action in PluginManager.Instance.Actions)
        {
            try
            {
                foreach (var keyword in action.Action.Keywords ?? Array.Empty<string>())
                    if (!string.IsNullOrWhiteSpace(keyword))
                        collected.Add(keyword.Trim());
            }
            catch (Exception ex)
            {
                Logger.Log($"[PluginTriggerQuery] an action's Keywords failed: {ex.Message}", LogLevel.Error);
            }
        }

        return collected;
    }

    /// <summary>
    /// Whether <paramref name="query"/> opens with one of <paramref name="keywords"/> as its entire first
    /// token followed by more typed text, in which case <paramref name="remainder"/> is what is left to
    /// search for. Case-insensitive, like every other keyword comparison here.
    ///
    /// Nothing is stripped unless a real term follows the keyword. Typing "cs" or "cs " on its own is still a
    /// legitimate file search for "cs": the word is what the user has, so far, asked to find, the provider
    /// answers alongside it either way, and an empty remainder would hand the engine a query it treats as
    /// "nothing to do" -- which in the quick window also suppresses the instant results this very keystroke
    /// is waiting for. Same activation rule FileFilterScopeResolver documents. "cs " (keyword plus the space that starts the term, term not yet typed) does match, and
    /// strips down to empty, which the caller already handles as "keep typing" rather than as a search.
    /// </summary>
    internal static bool Match(string query, IReadOnlyList<string> keywords, out string remainder)
    {
        remainder = query;
        if (string.IsNullOrEmpty(query) || keywords.Count == 0)
            return false;

        var trimmed = query.TrimStart();
        var spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex <= 0)
            return false;

        var firstToken = trimmed[..spaceIndex].Trim();
        if (firstToken.Length == 0)
            return false;

        for (var i = 0; i < keywords.Count; i++)
        {
            if (!string.Equals(firstToken, keywords[i], StringComparison.OrdinalIgnoreCase))
                continue;
            var rest = trimmed[(spaceIndex + 1)..].Trim();
            if (rest.Length == 0)
                return false;
            remainder = rest;
            return true;
        }

        return false;
    }
}
