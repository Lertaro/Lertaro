using Lertaro.Core.Hook.Commands;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.Core.Tests.Hook.Commands;

[TestClass]
public sealed class FileDialogCommandHandlerTests
{
    private static readonly IntPtr Dialog = new(42);
    private const string Folder = @"C:\收藏\";

    [TestMethod]
    public void Navigate_Favorite_RestoresFocusBeforeWritingTheExactPath()
    {
        var adapter = new RecordingAdapter();
        var foreground = new IntPtr(99); // Inline card initially owns the foreground.
        adapter.OnRestore = () => foreground = Dialog;

        Assert.IsTrue(FileDialogCommandHandler.Navigate(adapter, Dialog, Folder, true, () => foreground));

        CollectionAssert.AreEqual(new[] { "focus", "navigate" }, adapter.Calls);
        Assert.AreEqual(Dialog, adapter.Window);
        Assert.AreEqual(Folder, adapter.Path);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Navigate_FocusWasNotTransferred_DoesNotWritePath(bool reportedSuccess)
    {
        // Some adapters return true even when Windows refuses SetForegroundWindow.
        var adapter = new RecordingAdapter { RestoreResult = reportedSuccess };

        Assert.IsFalse(FileDialogCommandHandler.Navigate(adapter, Dialog, Folder, true, () => new IntPtr(99)));

        CollectionAssert.AreEqual(new[] { "focus" }, adapter.Calls);
        Assert.IsNull(adapter.Path);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Navigate_ExistingInlineRoute_PreservesAdapterResultWithoutRestoringFocus(bool result)
    {
        var adapter = new RecordingAdapter { NavigateResult = result };

        Assert.AreEqual(result, FileDialogCommandHandler.Navigate(adapter, Dialog, Folder, false,
            () => throw new AssertFailedException("The existing route must not query foreground.")));

        CollectionAssert.AreEqual(new[] { "navigate" }, adapter.Calls);
        Assert.AreEqual(Folder, adapter.Path);
    }

    [TestMethod]
    public void Navigate_AdapterDeclinesAfterFocus_ReturnsFailure()
    {
        var adapter = new RecordingAdapter { NavigateResult = false };
        Assert.IsFalse(FileDialogCommandHandler.Navigate(adapter, Dialog, Folder, true, () => Dialog));
        CollectionAssert.AreEqual(new[] { "focus", "navigate" }, adapter.Calls);
    }

    private sealed class RecordingAdapter : IFileDialogAdapter
    {
        public List<string> Calls { get; } = [];
        public bool RestoreResult { get; init; } = true;
        public bool NavigateResult { get; init; } = true;
        public Action? OnRestore { get; set; }
        public IntPtr Window { get; private set; }
        public string? Path { get; private set; }

        public bool RestoreFocus(IntPtr hwnd)
        {
            Calls.Add("focus");
            Window = hwnd;
            OnRestore?.Invoke();
            return RestoreResult;
        }

        public bool NavigateTo(IntPtr hwnd, string path)
        {
            Calls.Add("navigate");
            Window = hwnd;
            Path = path;
            return NavigateResult;
        }

        public bool CanHandle(IntPtr hwnd, string className, string processName) => true;
        public string? GetCurrentPath(IntPtr hwnd) => null;
        public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect) { rect = default; return false; }
    }
}
