namespace Lertaro.Cli.Search;

internal static class NonInteractiveSearchParser
{
    public const int DefaultLimit = 20;

    public static bool IsRequested(string[] args) => args.Any(arg =>
        string.Equals(arg, "--search", StringComparison.OrdinalIgnoreCase));

    public static bool TryParse(string[] args, out NonInteractiveSearchOptions? options, out string error)
    {
        options = null;
        error = string.Empty;
        if (!IsRequested(args))
            return false;

        string? query = null;
        string? directory = null;
        var limit = DefaultLimit;
        var json = false;
        var filesOnly = false;
        var foldersOnly = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--search":
                    if (!TryReadValue(args, ref i, "--search", out query, out error) || string.IsNullOrWhiteSpace(query))
                    {
                        error = string.IsNullOrEmpty(error) ? "--search requires a non-empty query." : error;
                        return true;
                    }
                    break;
                case "--limit":
                    if (!TryReadValue(args, ref i, "--limit", out var limitText, out error) ||
                        !int.TryParse(limitText, out limit) || limit < 1)
                    {
                        error = string.IsNullOrEmpty(error) ? "--limit requires a positive integer." : error;
                        return true;
                    }
                    break;
                case "--json":
                    json = true;
                    break;
                case "--files":
                    filesOnly = true;
                    break;
                case "--folders":
                    foldersOnly = true;
                    break;
                case "--path":
                    if (!TryReadValue(args, ref i, "--path", out directory, out error))
                        return true;
                    break;
                default:
                    error = $"Unknown search option: {args[i]}";
                    return true;
            }
        }

        if (query == null)
            error = "--search requires a query.";
        else if (filesOnly && foldersOnly)
            error = "--files and --folders cannot be used together.";
        else
            options = new NonInteractiveSearchOptions(query, limit, json, filesOnly, foldersOnly, directory);

        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, string option, out string? value, out string error)
    {
        value = null;
        error = string.Empty;
        if (index + 1 >= args.Length)
        {
            error = $"{option} requires a value.";
            return false;
        }

        value = args[++index];
        if (value.Length == 0)
            error = $"{option} requires a non-empty value.";
        return error.Length == 0;
    }
}
