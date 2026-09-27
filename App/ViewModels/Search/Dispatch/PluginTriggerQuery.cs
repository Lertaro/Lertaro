using Lertaro.Core;
using Lertaro.App.Services.Plugin;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.ViewModels.Search.Dispatch;

/// <summary>
/// Removes a leading trigger word from the text that gets searched against file and application names --
/// so "cs report" looks for files called "report" rather than fuzzy-matching "cs" and highlighting the
/// trigger inside every result row it dragged in.
/// </summary>
/// <remarks>
/// Every word the search box can answer to is inventoried here from live plugin state, so the host keeps no
/// copy of any of them: an instant provider's configured word
/// (<see cref="IInstantResultProvider.QueryTriggerKeywords"/>, read per call so a Settings change takes
/// effect on the next keystroke), a search action's own <em>Keywords</em> ("mkdir", "touch", "cmd"), a file
/// filter's scope keyword, and a per-type trigger character. The last two are collected for the collision
/// report only -- their own resolvers strip them, so stripping them here would strip twice.
///
/// The comparison itself is <see cref="TriggerWord"/>, shared with <see cref="KeywordMatcher"/> and
/// <see cref="FileFilterScopeResolver"/> and with every provider that owns a word, because the host and the
/// owner have to agree: a word the host strips but its owner does not recognise leaves the user with
/// neither the feature nor their search text.
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
    /// <param name="OwnerId">A stable identity for the owner (its assembly name, or a fixed id for the host's
    /// own settings), which the Settings page needs: it knows only the plugin it is configuring, never the
    /// localized display name an entry carries. Empty lets comparisons fall back to <paramref name="Owner"/>,
    /// which is what the pure rules in the tests use.</param>
    public readonly record struct Entry(string Word, string Owner, bool StripsFileSearch, string OwnerId = "");

    /// <summary>
    /// The other feature that already answers to <paramref name="word"/> -- its display name, or null when
    /// the word is free. <paramref name="selfOwnerId"/> is the asking component's own id: a plugin reusing
    /// one word across two of its own fields is not a clash between features, since the user sees one list
    /// of results either way.
    /// </summary>
    public static string? FindOtherOwner(string word, string selfOwnerId) =>
        FirstOtherOwner(Collect(), word, selfOwnerId);

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
            var ownerId = OwnerIdOf(provider);
            foreach (var keyword in declared)
                if (!string.IsNullOrWhiteSpace(keyword))
                    collected.Add(new Entry(TriggerWord.Normalize(keyword), provider.Name, true, ownerId));
        }

        // Search actions (mkdir / touch / cmd ...): KeywordMatcher treats "mkdir sub" as the action with
        // argument "sub", so the command word is a trigger by the same right -- and before they were
        // collected here the file list beside it was matched and highlighted against "mkdir sub", which is
        // neither what the argument means nor anything the user wanted.
        foreach (var action in PluginManager.Instance.Actions)
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
                collected.Add(new Entry(TriggerWord.Normalize(pair.Value), pair.Key, false, HostOwnerId));

        return collected;
    }

    // The host's own per-type triggers are one "component" as far as a clash is concerned.
    private const string HostOwnerId = "Lertaro.Settings.ResultTypeTriggers";

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
    public static bool ClaimsLeadingWord(string query)
    {
        if (string.IsNullOrEmpty(query))
            return false;

        foreach (var entry in Collect())
            if (entry.Word.Length > 1 && TriggerWord.TryMatch(query, entry.Word, out _))
                return true;

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

        var candidate = TriggerWord.Normalize(word);
        foreach (var entry in entries)
        {
            // An owner matching itself is a plugin reusing its own word across two of its own fields, not a
            // clash between features -- the user sees one list of results either way. Compared by component
            // identity when the entry carries one (that is what the Settings page can supply: it knows a
            // plugin's id, never the localized name an entry shows), falling back to the display name.
            var isSelf = entry.OwnerId.Length > 0
                ? string.Equals(entry.OwnerId, selfOwner, StringComparison.Ordinal)
                : string.Equals(entry.Owner, selfOwner, StringComparison.Ordinal);
            if (isSelf)
                continue;
            if (string.Equals(entry.Word, candidate, StringComparison.OrdinalIgnoreCase))
                return entry.Owner;
        }

        return null;
    }

    // One line per distinct set of collisions: a page refresh or a keystroke must not repeat the same
    // warning forever while the user has not decided what to rename. Guarded, because remembering it is a
    // read-compare-write on one piece of state and a search can be dispatched from more than one thread --
    // unsynchronised, two keystrokes can both see the old value and log the identical line. The decision is
    // taken under the lock and the write happens outside it, so no I/O runs while it is held.
    private static readonly object WarningGate = new();
    private static string? _warnedSignature;

    private static void WarnAboutCollisions(IReadOnlyList<Entry> entries)
    {
        var report = new List<string>();
        foreach (var entry in entries)
        {
            var other = FirstOtherOwner(entries, entry.Word, entry.OwnerId);
            // Report each pair once, from the owner that sorts first.
            if (other != null && string.CompareOrdinal(entry.Owner, other) < 0)
                report.Add($"{entry.Word}' ({entry.Owner} / {other})");
        }

        string? signatureToLog = null;
        lock (WarningGate)
        {
            if (report.Count == 0)
                _warnedSignature = null;
            else
            {
                var signature = string.Join("|", report);
                if (signature != _warnedSignature)
                {
                    _warnedSignature = signature;
                    signatureToLog = signature;
                }
            }
        }

        if (signatureToLog != null)
            Logger.Log($"[PluginTriggerQuery] more than one feature answers to the same trigger word, so the file search follows whichever registers first: {signatureToLog}", LogLevel.Warn);
    }
}
