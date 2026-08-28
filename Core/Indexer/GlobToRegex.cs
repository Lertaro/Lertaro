using System.Text;
using System.Text.RegularExpressions;

namespace Lertaro.Core;

public static class GlobToRegex
{
    // A backstop against a pattern that backtracks catastrophically, not a per-match budget. It only has
    // to be long enough that it can never be reached by an ordinary match, and a pattern that genuinely
    // runs away crosses any threshold on its very first input, so nothing is gained by keeping it tight.
    //
    // It was 50ms, which an ordinary match reached: matching a two-star glob against a short path threw
    // RegexMatchTimeoutException purely because the machine was busy compiling and running nineteen test
    // assemblies at once. That is worse than a flaky test -- NetworkGlobPattern.IsMatch catches the
    // timeout and returns false, so under load an exclusion rule silently stops excluding, and an ignore
    // pattern silently stops ignoring, with nothing but a log line to say so.
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    public static Regex Compile(string glob, bool ignoreCase = true, TimeSpan? matchTimeout = null)
    {
        var pattern = Convert(glob);
        var options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
        if (ignoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }
        return new Regex(pattern, options, matchTimeout ?? MatchTimeout);
    }

    public static string Convert(string glob)
    {
        if (string.IsNullOrEmpty(glob))
            return string.Empty;

        // Normalize path separators
        glob = glob.Replace('\\', '/');

        // Handle anchoring:
        // - If the glob starts with '/', it is anchored to the root.
        // - If it doesn't start with '/' but contains '/', it is also treated as relative to the root.
        // - Otherwise (no slashes), it matches the filename or folder name at any depth.
        var hasSlash = glob.TrimEnd('/').Contains('/');
        var startsWithSlash = glob.StartsWith("/");

        var sb = new StringBuilder();

        if (startsWithSlash)
        {
            glob = glob.Substring(1);
            sb.Append("^(?:[a-zA-Z]:)?[\\\\/]?");
        }
        else if (hasSlash)
        {
            sb.Append("^(?:[a-zA-Z]:)?[\\\\/]?");
        }
        else
        {
            sb.Append("(^|[\\\\/])");
        }

        var consecutiveStars = 0;
        void InsertStars()
        {
            if (consecutiveStars > 0)
            {
                if (consecutiveStars == 1)
                {
                    // Match a single path segment (non-separator characters)
                    sb.Append("[^\\\\/]*");
                }
                else
                {
                    // Two or more stars: match any character across multiple path segments
                    sb.Append(".*");
                }
                consecutiveStars = 0;
            }
        }

        var slashed = false;
        var inBrackets = 0;
        var inBraces = false;

        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];

            if (slashed)
            {
                sb.Append(Regex.Escape(c.ToString()));
                slashed = false;
                continue;
            }

            // Check for /**/
            if (c == '/' && i + 3 < glob.Length && glob.Substring(i, 4) == "/**/")
            {
                InsertStars();
                sb.Append("[\\\\/](?:.*[\\\\/])?");
                i += 3; // Skip "**/", next loop iteration will move past the slash
                continue;
            }

            // Check for **/ at start
            if (i == 0 && glob.Length >= 3 && glob.Substring(0, 3) == "**/")
            {
                sb.Append("(?:.*[\\\\/])?");
                i += 2;
                continue;
            }

            // Check for /** at end
            if (c == '/' && i + 2 < glob.Length && glob.Substring(i, 3) == "/**" && i + 3 == glob.Length)
            {
                InsertStars();
                sb.Append("[\\\\/].*");
                i += 2;
                continue;
            }

            if (c != '*')
            {
                InsertStars();
            }

            if (inBrackets > 0)
            {
                if (c == '[') inBrackets++;
                if (c == ']') inBrackets--;
                sb.Append(c);
                continue;
            }

            switch (c)
            {
                case '\\':
                    slashed = true;
                    break;
                case '*':
                    consecutiveStars++;
                    break;
                case '?':
                    sb.Append("[^\\\\/]");
                    break;
                case '[':
                    sb.Append('[');
                    inBrackets++;
                    break;
                case ']':
                    throw new ArgumentException("Mismatched ']' in glob: " + glob);
                case '{':
                    if (inBraces)
                        throw new ArgumentException("Nested '{' '}' not supported in glob: " + glob);
                    sb.Append("(?:");
                    inBraces = true;
                    break;
                case '}':
                    if (!inBraces)
                        throw new ArgumentException("Mismatched '}' in glob: " + glob);
                    sb.Append(')');
                    inBraces = false;
                    break;
                case ',':
                    if (inBraces)
                    {
                        sb.Append('|');
                    }
                    else
                    {
                        sb.Append(',');
                    }
                    break;
                // Escape standard regex characters that are not part of glob syntax
                case '.':
                case '+':
                case '(':
                case ')':
                case '|':
                case '^':
                case '$':
                case '@':
                case '%':
                    sb.Append('\\').Append(c);
                    break;
                case '/':
                    sb.Append("[\\\\/]");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        InsertStars();

        if (inBrackets > 0)
            throw new ArgumentException("Mismatched '[' and ']' in glob: " + glob);
        if (inBraces)
            throw new ArgumentException("Mismatched '{' and '}' in glob: " + glob);

        sb.Append("$");

        return sb.ToString();
    }
}
