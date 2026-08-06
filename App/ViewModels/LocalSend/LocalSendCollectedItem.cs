namespace Lertaro.App.ViewModels.LocalSend;

/// <summary>
/// Represents a collected file or directory item in Step 0 of LocalSendSendWindow.
/// ponytail: Split out to keep LocalSendSendViewModel.cs under 300 lines limit.
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
