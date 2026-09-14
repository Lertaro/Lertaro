namespace Lertaro.Cli.Search;

internal sealed record NonInteractiveSearchOptions(
    string Query,
    int Limit,
    bool Json,
    bool FilesOnly,
    bool FoldersOnly,
    string? DirectoryFilter);
