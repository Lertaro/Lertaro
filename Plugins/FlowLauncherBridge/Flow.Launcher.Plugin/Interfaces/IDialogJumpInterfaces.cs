using System.Windows.Controls;

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
}

public interface IDialogJumpExplorer : IFeatures, IDisposable
{
    IDialogJumpExplorerWindow? CheckExplorerWindow(IntPtr hwnd);
}

public interface IDialogJumpExplorerWindow : IDisposable
{
    IntPtr Handle { get; }
    IDialogJumpExplorerWindowTab GetCurrentTab();
}

public interface IDialogJumpExplorerWindowTab : IDisposable
{
    IntPtr Handle { get; }
    string GetCurrentFolder();
}

public interface IAsyncExternalPreview : IFeatures
{
    Task<Control?> GetExternalPreviewAsync(Result result, CancellationToken token);
}
