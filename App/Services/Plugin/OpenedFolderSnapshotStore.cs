namespace Lertaro.App.Services.Plugin;

internal sealed class OpenedFolderSnapshotStore
{
    private readonly object _gate = new();
    private IReadOnlyList<string> _paths = Array.Empty<string>();

    public void Update(IReadOnlyList<string> reportedPaths)
    {
        var paths = ExplorerPathValidator.FilterReportedDirectories(reportedPaths);
        lock (_gate)
            _paths = paths;
    }

    public IReadOnlyList<string> GetPaths()
    {
        lock (_gate)
            return _paths;
    }
}
