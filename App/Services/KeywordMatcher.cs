namespace Lertaro.App.Services;

public readonly record struct KeywordMatch(string Keyword, string ArgumentText);

public static class KeywordMatcher
{
    public static KeywordMatch? TryMatchKeyword(string query, IReadOnlyList<string> keywords)
    {
        var trimmed = query.Trim();
        var hasArgumentSeparator = trimmed.IndexOf(' ') >= 0;
        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                continue;

            var key = keyword.Trim();
            if (trimmed.Equals(key, StringComparison.OrdinalIgnoreCase))
                return new KeywordMatch(key, string.Empty);

            if (trimmed.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase))
                return new KeywordMatch(key, trimmed[(key.Length + 1)..].TrimStart());

            if (!hasArgumentSeparator && key.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
                return new KeywordMatch(key, string.Empty);
        }

        return null;
    }
}
