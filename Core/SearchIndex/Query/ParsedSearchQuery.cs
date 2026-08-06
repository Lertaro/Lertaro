namespace Lertaro.Core.SearchIndex.Query;

public struct ParsedSearchQuery
{
    public bool IsPathMode { get; }
    public string? TargetDrive { get; }
    public string? PathPatternLower { get; }
    public string? ExactPathLower { get; }
    public bool PathEndsWithSeparator { get; }

    public ParsedSearchQuery(
        bool isPathMode,
        string? targetDrive,
        string? pathPatternLower,
        string? exactPathLower,
        bool pathEndsWithSeparator = false)
    {
        IsPathMode = isPathMode;
        TargetDrive = targetDrive;
        PathPatternLower = pathPatternLower;
        ExactPathLower = exactPathLower;
        PathEndsWithSeparator = pathEndsWithSeparator;
    }
}
