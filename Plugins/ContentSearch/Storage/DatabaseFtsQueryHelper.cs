using System.Text;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Sanitizes and builds SQLite FTS5 MATCH query syntax from user search terms.
/// Split out to keep database management classes under the repository per-file line limit.
/// </summary>
public static class DatabaseFtsQueryHelper
{
    public static string BuildFtsQuery(string input)
    {
        var tokens = input.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var rawToken in tokens)
        {
            var token = rawToken.Replace("\"", "\"\"").Trim();
            if (token.Length == 0) continue;

            if (sb.Length > 0) sb.Append(" AND ");
            sb.Append('"').Append(token).Append("\"*");
        }
        return sb.ToString();
    }
}
