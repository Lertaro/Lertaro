using Lertaro.Plugins.BrowserData.Readers;

namespace Lertaro.Plugins.BrowserData.Tests.Readers;

[TestClass]
public sealed class SqliteCopyReaderTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void ReadCopy_PassesADifferentPathThanTheSource_ThatExistsAtCallTime()
    {
        using var dir = new TempDirectory();
        var source = Path.Combine(dir.Path, "History");
        File.WriteAllText(source, "original content");
        string? seenPath = null;
        var seenExists = false;

        SqliteCopyReader.ReadCopy(source, path =>
        {
            seenPath = path;
            seenExists = File.Exists(path);
            return new List<BrowserEntry>();
        });

        Assert.IsNotNull(seenPath);
        Assert.AreNotEqual(source, seenPath);
        Assert.IsTrue(seenExists);
    }

    [TestMethod]
    public void ReadCopy_CopyHasSameContentAsSource()
    {
        using var dir = new TempDirectory();
        var source = Path.Combine(dir.Path, "History");
        File.WriteAllText(source, "hello sqlite");
        string? seenContent = null;

        SqliteCopyReader.ReadCopy(source, path =>
        {
            seenContent = File.ReadAllText(path);
            return new List<BrowserEntry>();
        });

        Assert.AreEqual("hello sqlite", seenContent);
    }

    [TestMethod]
    public void ReadCopy_WalAndShmSidecars_AreCopiedAlongside()
    {
        using var dir = new TempDirectory();
        var source = Path.Combine(dir.Path, "History");
        File.WriteAllText(source, "main");
        File.WriteAllText(source + "-wal", "wal-data");
        File.WriteAllText(source + "-shm", "shm-data");
        var sidecarsSeen = false;

        SqliteCopyReader.ReadCopy(source, path =>
        {
            sidecarsSeen = File.Exists(path + "-wal") && File.Exists(path + "-shm")
                && File.ReadAllText(path + "-wal") == "wal-data"
                && File.ReadAllText(path + "-shm") == "shm-data";
            return new List<BrowserEntry>();
        });

        Assert.IsTrue(sidecarsSeen);
    }

    [TestMethod]
    public void ReadCopy_NoSidecarsPresent_StillCallsDelegateSuccessfully()
    {
        using var dir = new TempDirectory();
        var source = Path.Combine(dir.Path, "History");
        File.WriteAllText(source, "main");
        var called = false;

        SqliteCopyReader.ReadCopy(source, path => { called = true; return new List<BrowserEntry>(); });

        Assert.IsTrue(called);
    }

    [TestMethod]
    public void ReadCopy_TempFilesAreDeletedAfterward()
    {
        using var dir = new TempDirectory();
        var source = Path.Combine(dir.Path, "History");
        File.WriteAllText(source, "main");
        File.WriteAllText(source + "-wal", "wal-data");
        string? seenPath = null;

        SqliteCopyReader.ReadCopy(source, path => { seenPath = path; return new List<BrowserEntry>(); });

        Assert.IsNotNull(seenPath);
        Assert.IsFalse(File.Exists(seenPath));
        Assert.IsFalse(File.Exists(seenPath + "-wal"));
    }

    [TestMethod]
    public void ReadCopy_SourceFileDoesNotExist_ReturnsEmptyWithoutThrowing()
    {
        var result = SqliteCopyReader.ReadCopy(@"Z:\definitely-not-a-real-lertaro-file", _ => new List<BrowserEntry> { new("t", "https://x.com", false, 0) });

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void ReadCopy_ReadDelegateThrows_ReturnsEmptyInsteadOfPropagating()
    {
        using var dir = new TempDirectory();
        var source = Path.Combine(dir.Path, "History");
        File.WriteAllText(source, "main");

        var result = SqliteCopyReader.ReadCopy(source, _ => throw new InvalidOperationException("boom"));

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void ReadCopy_ReturnsWhatTheDelegateReturns()
    {
        using var dir = new TempDirectory();
        var source = Path.Combine(dir.Path, "History");
        File.WriteAllText(source, "main");
        var expected = new List<BrowserEntry> { new("Title", "https://x.com", true, 42) };

        var result = SqliteCopyReader.ReadCopy(source, _ => expected);

        CollectionAssert.AreEqual(expected, result);
    }
}
