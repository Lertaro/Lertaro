using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.CoreExtensions.Actions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Actions;

// These actions' CanExecute is a pure File/Directory.Exists check -- safe and real (temp files), unlike
// their Execute methods (real Clipboard/Shell IFileOperation calls), which stay untested.
[TestClass]
public sealed class ExistenceCheckActionsTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lertaro-tests-{Guid.NewGuid():N}.txt");

        public TempFile() => File.WriteAllText(Path, "x");

        public void Dispose()
        {
            try { File.Delete(Path); } catch { }
        }
    }

    [TestMethod]
    public void CopyFileAction_ExistingFile_CanExecuteTrue()
    {
        using var file = new TempFile();

        Assert.IsTrue(new CopyFileAction().CanExecute(new ISearchResult[] { new FakeResult { FullPath = file.Path } }));
    }

    [TestMethod]
    public void CopyFileAction_NonExistentFile_CanExecuteFalse() =>
        Assert.IsFalse(new CopyFileAction().CanExecute(new ISearchResult[] { new FakeResult { FullPath = @"Z:\definitely-not-real-lertaro-file.txt" } }));

    [TestMethod]
    public void CopyFileAction_EmptyResults_CanExecuteFalse() =>
        Assert.IsFalse(new CopyFileAction().CanExecute(Array.Empty<ISearchResult>()));

    [TestMethod]
    public void CopyPathAction_NonExistentFile_StillCanExecuteTrue() =>
        // Unlike the other actions, CopyPathAction only requires a non-empty path -- it never checks
        // File/Directory.Exists, since copying a path string works even for a target that no longer exists.
        Assert.IsTrue(new CopyPathAction().CanExecute(new ISearchResult[] { new FakeResult { FullPath = @"Z:\gone.txt" } }));

    [TestMethod]
    public void CopyPathAction_EmptyPath_CanExecuteFalse() =>
        Assert.IsFalse(new CopyPathAction().CanExecute(new ISearchResult[] { new FakeResult { FullPath = "" } }));

    [TestMethod]
    public void CutFileAction_ExistingFile_CanExecuteTrue()
    {
        using var file = new TempFile();

        Assert.IsTrue(new CutFileAction().CanExecute(new ISearchResult[] { new FakeResult { FullPath = file.Path } }));
    }

    [TestMethod]
    public void CutFileAction_NonExistentFile_CanExecuteFalse() =>
        Assert.IsFalse(new CutFileAction().CanExecute(new ISearchResult[] { new FakeResult { FullPath = @"Z:\gone.txt" } }));

    [TestMethod]
    public void DeleteFileAction_ExistingFile_CanExecuteTrue()
    {
        using var file = new TempFile();

        Assert.IsTrue(new DeleteFileAction().CanExecute(new ISearchResult[] { new FakeResult { FullPath = file.Path } }));
    }

    [TestMethod]
    public void DeleteFileAction_NonExistentFile_CanExecuteFalse() =>
        Assert.IsFalse(new DeleteFileAction().CanExecute(new ISearchResult[] { new FakeResult { FullPath = @"Z:\gone.txt" } }));

    [TestMethod]
    public void PermanentDeleteFileAction_ExistingFile_CanExecuteTrue()
    {
        using var file = new TempFile();

        Assert.IsTrue(new PermanentDeleteFileAction().CanExecute(new ISearchResult[] { new FakeResult { FullPath = file.Path } }));
    }

    [TestMethod]
    public void PermanentDeleteFileAction_NonExistentFile_CanExecuteFalse() =>
        Assert.IsFalse(new PermanentDeleteFileAction().CanExecute(new ISearchResult[] { new FakeResult { FullPath = @"Z:\gone.txt" } }));

    [TestMethod]
    public void LocateInExplorerAction_ExistingFile_CanExecuteTrue()
    {
        using var file = new TempFile();

        Assert.IsTrue(new LocateInExplorerAction().CanExecute(new ISearchResult[] { new FakeResult { FullPath = file.Path } }));
    }

    [TestMethod]
    public void LocateInExplorerAction_NonExistentFile_CanExecuteFalse() =>
        Assert.IsFalse(new LocateInExplorerAction().CanExecute(new ISearchResult[] { new FakeResult { FullPath = @"Z:\gone.txt" } }));

    [TestMethod]
    public void OpenResultAction_ExistingFile_CanExecuteTrue()
    {
        using var file = new TempFile();

        Assert.IsTrue(new OpenResultAction().CanExecute(new ISearchResult[] { new FakeResult { FullPath = file.Path } }));
    }

    [TestMethod]
    public void OpenResultAction_NonExistentFile_CanExecuteFalse() =>
        Assert.IsFalse(new OpenResultAction().CanExecute(new ISearchResult[] { new FakeResult { FullPath = @"Z:\gone.txt" } }));

    [TestMethod]
    public void OpenResultAsAdminAction_ExistingFile_CanExecuteTrue()
    {
        using var file = new TempFile();

        Assert.IsTrue(new OpenResultAsAdminAction().CanExecute(new ISearchResult[] { new FakeResult { FullPath = file.Path, IsDir = false } }));
    }

    [TestMethod]
    public void OpenResultAsAdminAction_Directory_CanExecuteFalse()
    {
        // Requires a FILE specifically -- "run as admin" on a directory doesn't make sense.
        var dir = Directory.CreateTempSubdirectory("lertaro-tests-");
        try
        {
            Assert.IsFalse(new OpenResultAsAdminAction().CanExecute(new ISearchResult[] { new FakeResult { FullPath = dir.FullName, IsDir = true } }));
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); } catch { }
        }
    }
}
