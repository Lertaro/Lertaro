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
    // (network/WSL/folder-index drives via DriveRefreshRunner), FromMachineSettings for the local drive
    // walk the --service process runs, which cannot read the per-user settings file at all. MaxDepth/
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

    public static WalkOptions FromMachineSettings(MachineSettings settings) => From(
        settings.ExcludedPaths,
        settings.IgnoredPathGlobs,
        settings.IgnoredPathRegexes);
}

internal readonly record struct NetworkDriveWalkStats(
    int Skipped,
    int Errors,
    int EnumerateErrors,
    int AttributeErrors,
    int ReparseSkipped,
    int SlowDirectories);
