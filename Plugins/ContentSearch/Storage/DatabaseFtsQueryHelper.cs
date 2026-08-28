using System.Text;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Sanitizes and builds SQLite FTS5 trigram MATCH query syntax from user search terms.
/// Split out to keep database management classes under the repository per-file line limit.
/// </summary>
public static class DatabaseFtsQueryHelper
{
    private static readonly char[] TrimChars = ['"', '*', '^', ':', '(', ')', '{', '}', '[', ']'];

    public static string BuildFtsQuery(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var tokens = input.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var rawToken in tokens)
        {
            var cleaned = rawToken.Trim(TrimChars).Replace("\"", "\"\"").Trim();
            if (cleaned.Length == 0) continue;

            if (sb.Length > 0) sb.Append(" AND ");

            // In FTS5 trigram, 1 or 2 character tokens require a trailing '*' for prefix matching
            if (cleaned.Length < 3)
            {
                sb.Append('"').Append(cleaned).Append("\"*");
            }
            else
            {
                sb.Append('"').Append(cleaned).Append('"');
            }
        }
        return sb.ToString();
    }
}
