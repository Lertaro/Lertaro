using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendReceiveProgressTests
{
    [TestMethod]
    public void Update_SelectedFilesAndRetry_CountsOnlyCurrentAttemptBytes()
    {
        var files = new Dictionary<string, LocalSendFileDto>
        {
            ["skipped"] = new() { Size = 100 }, ["a"] = new() { Size = 10 }, ["b"] = new() { Size = 20 }
        };
        var progress = new LocalSendReceiveProgress(files, new HashSet<string> { "a", "b" });
        Assert.AreEqual(30L, progress.TotalBytes);
        Assert.AreEqual(2, progress.TotalFiles);
        Assert.AreEqual(1, progress.FileIndex("a"));
        Assert.AreEqual(10L, progress.Update("a", 10));
        Assert.AreEqual(15L, progress.Update("b", 5));
        Assert.AreEqual(5L, progress.Update("a", 0));
        Assert.AreEqual(15L, progress.Update("a", 10));
        Assert.AreEqual(1, progress.Complete("a"));
        Assert.AreEqual(1, progress.Complete("a"));
        Assert.AreEqual(30L, progress.Update("b", 20));
        Assert.AreEqual(2, progress.Complete("b"));
    }

    [TestMethod]
    public void Update_ParallelFiles_PreservesEveryByteAndCompletion()
    {
        var files = Enumerable.Range(0, 1000).ToDictionary(i => i.ToString(), _ => new LocalSendFileDto { Size = 20 });
        var progress = new LocalSendReceiveProgress(files, null);
        Parallel.ForEach(files.Keys, id =>
        {
            progress.Update(id, 10);
            progress.Update(id, 20);
            progress.Complete(id);
        });
        Assert.AreEqual(20000L, progress.Update("0", 20));
        Assert.AreEqual(1000, progress.Complete("0"));
    }
}
