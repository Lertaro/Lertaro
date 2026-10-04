namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

internal sealed record WalkOptions(
    IReadOnlyList<string> ExcludedPaths,
    IReadOnlyList<string> IgnoredPathGlobs,
    IReadOnlyList<string> IgnoredPathRegexes,
    int MaxDepth,
    int WorkerCount,
    bool UseIgnoreFiles)
{
    // The one construction of a walk's exclusion/ignore field list: every walk that honours the rules
    // goes through here, so a local drive can never end up filtered by a different set of rules than a
    // network drive. Only the source differs -- FromUserSettings for the paths that run in the App
    // (network/WSL/folder-index drives via DriveRefreshRunner), and, for the local drive walk the
    // --service process runs, the rules the App sends it over SetMachineSettings (see
    // UsnServicePipeRequestProcessor and UsnIndexer.WalkOptions). MaxDepth/
    // WorkerCount stay 0 (unlimited / auto) and ignore files (.gitignore/.ignore/.fdignore) stay enabled,
    // which is what the network path has always used.
    public static WalkOptions From(
        IReadOnlyList<string> excludedPaths,
        IReadOnlyList<string> ignoredPathGlobs,
        IReadOnlyList<string> ignoredPathRegexes) => new(
            excludedPaths,
            ignoredPathGlobs,
            ignoredPathRegexes,
            MaxDepth: 0,
            WorkerCount: 0,
            UseIgnoreFiles: true);

    public static WalkOptions FromUserSettings(UserSettings settings) => From(
        settings.ExcludedPaths,
        settings.IgnoredPathGlobs,
        settings.IgnoredPathRegexes);

    /// <summary>
    /// What a walk filters by when the sender supplied no rules at all.
    /// </summary>
    /// <remarks>
    /// A service that has never been told a rule must not invent one: filtering nothing is the upstream
    /// default, and the only alternative -- rules from some other source -- would exclude paths the user
    /// never configured. This is the state <see cref="UsnIndexer.WalkOptions"/> starts in.
    /// </remarks>
    public static WalkOptions Empty { get; } = From([], [], []);
}

internal readonly record struct NetworkDriveWalkStats(
    int Skipped,
    int Errors,
    int EnumerateErrors,
    int AttributeErrors,
    int ReparseSkipped,
    int SlowDirectories);
