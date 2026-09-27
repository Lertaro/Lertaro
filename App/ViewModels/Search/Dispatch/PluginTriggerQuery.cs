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
    /// <summary>One trigger word plus the component that owns it.</summary>
    /// <param name="StripsFileSearch">Whether the host takes this word off the file/application search. The
    /// other group already strips its own elsewhere (a file-filter keyword in FileFilterScopeResolver, a
    /// per-type trigger in ResultTypeTriggerHandler), so listing them as strippers would strip twice.</param>
    public readonly record struct Entry(string Word, string Owner, bool StripsFileSearch);

    public static string Strip(string query)
    {
        var entries = Collect();
        WarnAboutCollisions(entries);

        var words = new List<string>();
        foreach (var entry in entries)
            if (entry.StripsFileSearch)
                words.Add(entry.Word);

        return Match(query, words, out var remainder) ? remainder : query;
    }

    /// <summary>
    /// Every trigger word the search box currently recognises, with its owner. Collected from live plugin
    /// state so the host keeps no copy of any of them, and shared by the strip rule and the collision report
    /// -- two collectors would drift, and the Settings warning would stop matching what actually happens.
    /// </summary>
    public static IReadOnlyList<Entry> Collect()
    {
        var collected = new List<Entry>();

        // Instant providers: each reads its own configured word(s) out of its plugin settings, so the host
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
                    collected.Add(new Entry(keyword.Trim(), provider.Name, true));
        }

        // Search actions (mkdir / touch / cmd ...): KeywordMatcher already treats "mkdir sub" as the action
        // with argument "sub", so the command word is a trigger by the same right -- and today the file list
        // beside it is matched and highlighted against "mkdir sub", which is neither what the argument means
        // nor anything the user wanted.
        foreach (var action in PluginManager.Instance.Actions)
        {
            try
            {
                foreach (var keyword in action.Action.Keywords ?? Array.Empty<string>())
                    if (!string.IsNullOrWhiteSpace(keyword))
                        collected.Add(new Entry(keyword.Trim(), action.Action.GetType().Name, true));
            }
            catch (Exception ex)
            {
                Logger.Log($"[PluginTriggerQuery] an action's Keywords failed: {ex.Message}", LogLevel.Error);
            }
        }

        // File-filter scope keywords and per-type triggers: not stripped here (their own resolver owns that),
        // but they collide with everything above, so they belong in the report.
        foreach (var provider in PluginManager.Instance.SearchScopeProviders)
        {
            try
            {
                foreach (var scope in provider.GetSearchScopes() ?? Array.Empty<SearchScope>())
                    if (!string.IsNullOrWhiteSpace(scope.Keyword))
                        collected.Add(new Entry(scope.Keyword.Trim(), provider.Name, false));
            }
            catch (Exception ex)
            {
                Logger.Log($"[PluginTriggerQuery] {provider.GetType().Name}.GetSearchScopes failed: {ex.Message}", LogLevel.Error);
            }
        }

        foreach (var pair in UserSettings.Load().ResultTypeTriggers ?? new Dictionary<string, string>())
            if (!string.IsNullOrWhiteSpace(pair.Value))
                collected.Add(new Entry(pair.Value.Trim(), pair.Key, false));

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
    /// is waiting for. Same activation rule FileFilterScopeResolver documents.
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

    /// <summary>
    /// Who else already owns this word, or null when it is free. Pure, so a Settings field can warn on the
    /// same rule the log line uses instead of a second approximation of it.
    /// </summary>
    internal static string? FirstOtherOwner(IReadOnlyList<Entry> entries, string word, string selfOwner)
    {
        if (string.IsNullOrWhiteSpace(word))
            return null;

        var candidate = word.Trim();
        foreach (var entry in entries)
        {
            // An owner matching itself is a plugin reusing its own word across two of its own fields, not a
            // clash between features -- the user sees one list of results either way.
            if (string.Equals(entry.Owner, selfOwner, StringComparison.Ordinal))
                continue;
            if (string.Equals(entry.Word, candidate, StringComparison.OrdinalIgnoreCase))
                return entry.Owner;
        }

        return null;
    }

    // One line per distinct set of collisions: a page refresh or a keystroke must not repeat the same
    // warning forever while the user has not decided what to rename.
    private static string? _warnedSignature;

    private static void WarnAboutCollisions(IReadOnlyList<Entry> entries)
    {
        var report = new List<string>();
        foreach (var entry in entries)
        {
            var other = FirstOtherOwner(entries, entry.Word, entry.Owner);
            // Report each pair once, from the owner that sorts first.
            if (other != null && string.CompareOrdinal(entry.Owner, other) < 0)
                report.Add($"{entry.Word}' ({entry.Owner} / {other})");
        }

        if (report.Count == 0)
        {
            _warnedSignature = null;
            return;
        }

        var signature = string.Join("|", report);
        if (signature == _warnedSignature)
            return;
        _warnedSignature = signature;
        Logger.Log($"[PluginTriggerQuery] more than one feature answers to the same trigger word, so the file search follows whichever registers first: {signature}", LogLevel.Warn);
    }
}
