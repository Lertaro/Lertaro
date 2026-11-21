namespace Lertaro.App.ViewModels.LocalSend;

/// <summary>
/// A file or directory collected in the first step of LocalSendSendWindow.
/// </summary>
public sealed class LocalSendCollectedItem
{
    public LocalSendCollectedItem(string path, bool isFolder)
    {
        Path = path;
        IsFolder = isFolder;
    }

    public string Path { get; }
    public bool IsFolder { get; }
    public string DisplayName => System.IO.Path.GetFileName(Path) is { Length: > 0 } name ? name : Path;
}
