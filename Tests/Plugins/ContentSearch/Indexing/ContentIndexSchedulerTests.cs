using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

[TestClass]
public sealed class ContentIndexSchedulerTests
{
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), "TestIndexScheduler_" + Guid.NewGuid().ToString("N") + ".db");
        _database = new ContentSearchDatabase(_tempDbPath);
        _database.Initialize();
    }

    [TestCleanup]
    public void TearDown()
    {
        _database.Dispose();
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
    }

    [TestMethod]
    public void IsFileInMonitoredFolders_CorrectlyValidatesPaths()
    {
        using var scheduler = new ContentIndexScheduler(_database);
        var config = new ContentIndexConfig
        {
            MonitoredFolders = new List<string> { @"C:\MyDocs", @"D:\Workspace\Projects" }
        };
        scheduler.UpdateConfig(config);

        Assert.IsTrue(scheduler.IsFileInMonitoredFolders(@"C:\MyDocs\test.txt"));
        Assert.IsTrue(scheduler.IsFileInMonitoredFolders(@"C:\MyDocs\SubDir\document.docx"));
        Assert.IsTrue(scheduler.IsFileInMonitoredFolders(@"D:\Workspace\Projects\src\App.cs"));

        Assert.IsFalse(scheduler.IsFileInMonitoredFolders(@"C:\OtherFolder\file.txt"));
        Assert.IsFalse(scheduler.IsFileInMonitoredFolders(@"C:\MyDocsOther\file.txt"));
        Assert.IsFalse(scheduler.IsFileInMonitoredFolders(@"Z:\Data\test.md"));
    }
}
