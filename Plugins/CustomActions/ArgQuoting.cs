using System.Text;

namespace Lertaro.Plugins.CustomActions;

/// <summary>
/// Quotes a single value so it survives as exactly one argument when passed via
/// ProcessStartInfo.Arguments (a single command-line string on Windows).
/// Follows the CommandLineToArgvW / C runtime convention. Duplicated per plugin
/// on purpose — the plugins ship as independent DLLs and don't share a library.
/// </summary>
internal static class ArgQuoting
{
    public static string Quote(string value)
    {
        // No quoting needed when the value has no whitespace or quote and isn't empty.
        if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            return value;

        var sb = new StringBuilder();
        sb.Append('"');
        for (var i = 0; i < value.Length;)
        {
            var c = value[i++];
            if (c == '\\')
            {
                var backslashes = 1;
                while (i < value.Length && value[i] == '\\') { i++; backslashes++; }
                if (i == value.Length)
                    sb.Append('\\', backslashes * 2);       // before closing quote: double them
                else if (value[i] == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);   // escape the run and the quote
                    sb.Append('"');
                    i++;
                }
                else
                    sb.Append('\\', backslashes);           // ordinary backslashes, leave as-is
            }
            else if (c == '"')
            {
                sb.Append('\\').Append('"');
            }
            else
            {
                sb.Append(c);
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
