namespace Lertaro.Core.SearchIndex.Query;

public static class SearchQueryParser
{
    public static ParsedSearchQuery Parse(string query)
    {
        var normalizedQuery = NormalizePathSeparators(query.Trim()).ToLowerInvariant();
        if (ContainsPathSeparator(normalizedQuery))
        {
            string? pathTargetDrive = null;
            var pathPatternLower = normalizedQuery;

            if (TryNormalizeDrivePath(normalizedQuery, out var drive, out var normalizedDrivePath))
            {
                pathTargetDrive = drive;
                pathPatternLower = normalizedDrivePath;
            }

            var pathEndsWithSeparator = pathPatternLower.EndsWith(Path.DirectorySeparatorChar);
            var exactPathLower = NormalizeExactPath(pathPatternLower);

            return new ParsedSearchQuery(
                isPathMode: true,
                pathTargetDrive,
                pathPatternLower,
                exactPathLower,
                pathEndsWithSeparator);
        }

        string? targetDrive = null;

        var rawTerms = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawTerm in rawTerms)
        {
            if (rawTerm.Length >= 2 && char.IsLetter(rawTerm[0]) && rawTerm[1] == Path.VolumeSeparatorChar)
            {
                targetDrive = rawTerm[0].ToString();
            }
        }

        return new ParsedSearchQuery(
            isPathMode: false,
            targetDrive,
            pathPatternLower: null,
            exactPathLower: null);
    }

    private static bool ContainsPathSeparator(string text) => text.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
               (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar &&
                text.IndexOf(Path.AltDirectorySeparatorChar) >= 0);

    private static string NormalizePathSeparators(string text) => Path.AltDirectorySeparatorChar == Path.DirectorySeparatorChar
            ? text
            : text.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static bool TryNormalizeDrivePath(string path, out string? drive, out string normalizedPath)
    {
        drive = null;
        normalizedPath = path;

        if (path.Length < 2 || !char.IsLetter(path[0]))
            return false;

        if (path[1] == Path.VolumeSeparatorChar)
        {
            drive = path[0].ToString();
            normalizedPath = drive + Path.VolumeSeparatorChar + path.Substring(2);
            return true;
        }

        if (path[1] == Path.DirectorySeparatorChar)
        {
            drive = path[0].ToString();
            normalizedPath = drive + Path.VolumeSeparatorChar + Path.DirectorySeparatorChar + path.Substring(2).TrimStart(Path.DirectorySeparatorChar);
            return true;
        }

        return false;
    }

    public static string NormalizeExactPath(string pathLower)
    {
        var minLength = 0;
        if (pathLower.Length >= 3 &&
            char.IsLetter(pathLower[0]) &&
            pathLower[1] == Path.VolumeSeparatorChar &&
            pathLower[2] == Path.DirectorySeparatorChar)
        {
            minLength = 3;
        }

        var end = pathLower.Length;
        while (end > minLength && pathLower[end - 1] == Path.DirectorySeparatorChar)
        {
            end--;
        }

        return end == pathLower.Length ? pathLower : pathLower.Substring(0, end);
    }
}
