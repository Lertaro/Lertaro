using Lertaro.PluginSdk.Services;

namespace Lertaro.App.Services;

public readonly record struct KeywordMatch(string Keyword, string ArgumentText);

/// <summary>
/// Matches a search action's command word against what the user typed: "mkdir sub" is the action "mkdir"
/// with the argument "sub", and "mk" on the way there is the same action offered as a completion.
/// </summary>
/// <remarks>
/// The token/argument rule itself lives in <see cref="TriggerWord"/>, shared with the host's own stripping
/// of trigger words (<c>PluginTriggerQuery</c>) and with every instant provider -- the three used to be
/// separate copies with separate ideas about casing, padding and what counted as a separator, so a word
/// could be stripped from the file search by one and left in place by another.
///
/// Where an action is deliberately MORE permissive than the file-search strip: a bare command word activates
/// ("mkdir" alone is the action with no argument yet), because the row is the thing the user asked for,
/// while the file search beside it keeps the word as searchable text.
/// </remarks>
public static class KeywordMatcher
{
    public static KeywordMatch? TryMatchKeyword(string query, IReadOnlyList<string> keywords)
    {
        foreach (var keyword in keywords)
        {
            var key = TriggerWord.Normalize(keyword);
            if (key.Length == 0)
                continue;

            if (TriggerWord.TryMatch(query, key, out var argument))
                return new KeywordMatch(key, argument);

            if (TriggerWord.IsTypedPrefixOf(query, key))
                return new KeywordMatch(key, string.Empty);
        }

        return null;
    }
}
