namespace Flow.Launcher.Plugin;

public interface IAsyncDialogJump : IFeatures
{
    Task<List<DialogJumpResult>> QueryDialogJumpAsync(Query query, CancellationToken token);
}

public interface IDialogJump : IAsyncDialogJump
{
    List<DialogJumpResult> QueryDialogJump(Query query);
    Task<List<DialogJumpResult>> IAsyncDialogJump.QueryDialogJumpAsync(Query query, CancellationToken token) => Task.Run(() => QueryDialogJump(query), token);
}

public interface IDialogJumpDialog : IFeatures, IDisposable
{
    IDialogJumpDialogWindow? CheckDialogWindow(IntPtr hwnd);
}

public interface IDialogJumpDialogWindow : IDisposable
{
    IntPtr Handle { get; }
    IDialogJumpDialogWindowTab GetCurrentTab();
}

public interface IDialogJumpDialogWindowTab : IDisposable
{
    IntPtr Handle { get; }
    string GetCurrentFolder();
    string GetCurrentFile();
    bool JumpFolder(string path, bool auto);
    bool JumpFile(string path);
    bool Open();
}

public interface IDialogJumpExplorer : IFeatures, IDisposable
{
    IDialogJumpExplorerWindow? CheckExplorerWindow(IntPtr hwnd);
}

public interface IDialogJumpExplorerWindow : IDisposable
{
    IntPtr Handle { get; }
    string? GetExplorerPath();
}

public interface IDialogJumpExplorerWindowTab : IDisposable
{
    IntPtr Handle { get; }
    string GetCurrentFolder();
}

public interface IAsyncExternalPreview : IFeatures
{
    Task OpenPreviewAsync(string path, bool sendFailToast = true);
    Task ClosePreviewAsync();
    Task SwitchPreviewAsync(string path, bool sendFailToast = true);
    bool AllowAlwaysPreview();
}
