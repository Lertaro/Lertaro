using Lertaro.Core;
using Lertaro.App.Services.Plugin;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.ViewModels.Search.Dispatch;

/// <summary>
/// Removes a plugin's leading trigger word from the text that gets searched against file and application
/// names -- so "cs report" looks for files called "report" rather than fuzzy-matching "cs" and highlighting
/// the trigger inside every result row it dragged in.
/// </summary>
/// <remarks>
/// Same shape as <see cref="FileFilterScopeResolver"/>: a thin collector over the live plugin registry, and
/// a pure <see cref="Match"/> holding the activation rules, so the rules are testable without PluginManager.
/// The words come from each provider's own <see cref="IInstantResultProvider.QueryTriggerKeywords"/>, which
/// is the user's configured value read per call -- the host never carries a copy of any plugin's keyword.
///
/// Providers themselves still receive the untouched box text (see SearchExecutionEngine's instantQuery),
/// because the provider that owns a word has to keep recognising it; what this changes is only what the
/// OTHER results are matched and highlighted against.
/// </remarks>
internal static class InstantTriggerQuery
{
    public static string Strip(string query)
    {
        var keywords = CollectKeywords();
        return Match(query, keywords, out var remainder) ? remainder : query;
    }

    private static IReadOnlyList<string> CollectKeywords()
    {
        var collected = new List<string>();
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
                Logger.Log($"[InstantTriggerQuery] {provider.GetType().Name}.QueryTriggerKeywords failed: {ex.Message}", LogLevel.Error);
                continue;
            }

            if (declared == null) continue;
            foreach (var keyword in declared)
                if (!string.IsNullOrWhiteSpace(keyword))
                    collected.Add(keyword.Trim());
        }

        return collected;
    }

    /// <summary>
    /// Whether <paramref name="query"/> opens with one of <paramref name="keywords"/> as its entire first
    /// token followed by more typed text, in which case <paramref name="remainder"/> is what is left to
    /// search for. Case-insensitive, like every other keyword comparison here.
    ///
    /// A bare keyword with nothing after it deliberately does NOT match: typing "cs" on its own is still a
    /// legitimate file search for "cs", and the file-filter scope above uses the same rule for exactly the
    /// same reason. "cs " (keyword plus the space that starts the term, term not yet typed) does match, and
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
            remainder = trimmed[(spaceIndex + 1)..].Trim();
            return true;
        }

        return false;
    }
}
