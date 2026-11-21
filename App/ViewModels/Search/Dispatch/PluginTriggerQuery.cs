using Lertaro.Core;
using Lertaro.App.Services.Plugin;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

using SearchWindowType = Lertaro.PluginSdk.Abstractions.SearchWindowType;

namespace Lertaro.App.ViewModels.Search.Dispatch;

/// <summary>
/// Removes a leading plugin trigger from file and application queries: "cs report" searches their
/// names for "report". Providers still receive the original query so they can recognize their trigger.
/// </summary>
/// <remarks>
/// Read configured instant-provider triggers and visible search-action keywords from live plugin state
/// on every call. Strip a trigger only in windows where its owner is available and only when a term
/// follows it. Thus a command word alone remains searchable as text.
///
/// Filter-scope keywords and result-type characters are collected for collision reporting only;
/// their own resolvers strip them. <see cref="Collect()"/> includes all windows for Settings warnings.
///
/// Use <see cref="TriggerWord"/> throughout the host and providers so activation and stripping agree.
/// <see cref="Match"/> holds the rules separately from plugin discovery for testing.
/// </remarks>
internal static class PluginTriggerQuery
{
    /// <summary>
    /// One trigger word plus the component that owns it.
    /// </summary>
    /// <param name="StripsFileSearch">Whether the host takes this word off the file/application search. The
    /// other group already strips its own elsewhere (a file-filter keyword in FileFilterScopeResolver, a
    /// per-type trigger in ResultTypeTriggerHandler), so listing them as strippers would strip twice.</param>
    /// <param name="OwnerId">A stable identity for the owner (its assembly name, or a fixed id for the host's
    /// own settings), which the Settings page needs: it knows only the plugin it is configuring, never the
    /// localized display name an entry carries. Empty lets comparisons fall back to <paramref name="Owner"/>,
    /// which is what the pure rules in the tests use.</param>
    public readonly record struct Entry(string Word, string Owner, bool StripsFileSearch, string OwnerId = "");

    public static string Strip(string query, SearchWindowType windowType)
    {
        var entries = Collect(windowType);
        PluginTriggerCollisionReport.WarnAboutCollisions(entries);

        var words = new List<string>();
        foreach (var entry in entries)
            if (entry.StripsFileSearch)
                words.Add(entry.Word);

        return Match(query, words, out var remainder) ? remainder : query;
    }

    /// <summary>
    /// Every trigger word the search box recognises in <paramref name="windowType"/>, with its owner. Use
    /// this where the answer decides what the user's own typed text means (the strip, the trigger-character
    /// precedence); use the window-agnostic <see cref="Collect()"/> where the answer is only about which
    /// features exist (the Settings collision warning).
    /// </summary>
    public static IReadOnlyList<Entry> Collect(SearchWindowType windowType) => CollectCore(windowType);

    /// <summary>
    /// Every trigger word the search box recognises in ANY window, with its owner. Collected from live
    /// plugin state so the host keeps no copy of any of them, and shared by the strip rule and the collision
    /// report -- two collectors would drift, and the Settings warning would stop matching what actually
    /// happens.
    /// </summary>
    public static IReadOnlyList<Entry> Collect() => CollectCore(null);

    private static IReadOnlyList<Entry> CollectCore(SearchWindowType? windowType)
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
            var ownerId = OwnerIdOf(provider);
            foreach (var keyword in declared)
                if (!string.IsNullOrWhiteSpace(keyword))
                    collected.Add(new Entry(TriggerWord.Normalize(keyword), provider.Name, true, ownerId));
        }

        // Search actions (mkdir / touch / cmd ...): KeywordMatcher treats "mkdir sub" as the action with
        // argument "sub", so the command word is a trigger by the same right -- and before they were
        // collected here the file list beside it was matched and highlighted against "mkdir sub", which is
        // neither what the argument means nor anything the user wanted. Only the ones this window can
        // actually offer: a word whose row is never shown here (an inline-only action in the quick window)
        // must not take text out of the user's search.
        foreach (var action in windowType is { } type ? PluginManager.Instance.ActionsVisibleIn(type) : PluginManager.Instance.Actions)
        {
            try
            {
                foreach (var keyword in action.Action.Keywords ?? Array.Empty<string>())
                    if (!string.IsNullOrWhiteSpace(keyword))
                        collected.Add(new Entry(TriggerWord.Normalize(keyword), action.Action.GetType().Name, true, OwnerIdOf(action.Plugin)));
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
                        collected.Add(new Entry(TriggerWord.Normalize(scope.Keyword), provider.Name, false, OwnerIdOf(provider)));
            }
            catch (Exception ex)
            {
                Logger.Log($"[PluginTriggerQuery] {provider.GetType().Name}.GetSearchScopes failed: {ex.Message}", LogLevel.Error);
            }
        }

        foreach (var pair in UserSettings.Load().ResultTypeTriggers ?? new Dictionary<string, string>())
            if (!string.IsNullOrWhiteSpace(pair.Value))
                collected.Add(new Entry(TriggerWord.Normalize(pair.Value), pair.Key, false, PluginTriggerCollisionReport.HostTriggerOwnerId));

        return collected;
    }

    // A plugin's assembly name is what the settings page knows about it (its PluginId), so this is the one
    // identity that lets a field tell its own plugin's words apart from somebody else's.
    private static string OwnerIdOf(object component) => component.GetType().Assembly.GetName().Name ?? string.Empty;

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

        // Which characters separate a word from its term, and where the term starts, is TriggerWord's
        // call -- the same answer the word's own owner gives. The only policy held here is that a word
        // with nothing after it is not stripped.
        if (!TriggerWord.TryMatchAny(query, keywords, out _, out var term) || term.Length == 0)
            return false;

        remainder = term;
        return true;
    }

    /// <summary>
    /// Whether a registered multi-character word owns <paramref name="query"/>'s first token, with or
    /// without a term after it. The quick window consults this before applying a per-type trigger
    /// CHARACTER, which only ever inspects the first character it is given: configure "s" as a type trigger
    /// and "set 路径" is cut down to "et 路径" -- the word its owner still needs is gone, the file search
    /// runs on garbage, and the collision report never sees it because it compares whole words. A word
    /// beats a character for the same reason a more specific prefix beats a less specific one.
    ///
    /// Multi-character entries only: a single-character entry IS the type-trigger family this is guarding
    /// against, so letting it claim the token would put us back where we started.
    /// </summary>
    public static bool ClaimsLeadingWord(string query, SearchWindowType windowType) =>
        ClaimsLeadingWord(query, Collect(windowType));

    /// <summary>
    /// The rule above, over an inventory the caller already holds -- pure, so a test can pin the
    /// word-beats-character precedence (and the multi-character cutoff) without a plugin registry.
    /// </summary>
    internal static bool ClaimsLeadingWord(string query, IReadOnlyList<Entry> entries)
    {
        if (string.IsNullOrEmpty(query))
            return false;

        foreach (var entry in entries)
            if (entry.Word.Length > 1 && TriggerWord.TryMatch(query, entry.Word, out _))
                return true;

        return false;
    }
}
