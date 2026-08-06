namespace Lertaro.Core.Tests.Indexer;

[TestClass]
public sealed class FileRecordStoreSerializerTests
{
    [TestMethod]
    public void GetBasePath_LowercasesSourceKeyAndJoinsWithCacheDir()
    {
        var result = FileRecordStoreSerializer.GetBasePath(@"c:\cache", "NETWORK_DRIVE_Z");

        Assert.AreEqual(Path.Combine(@"c:\cache", "network_drive_z"), result);
    }

    [TestMethod]
    public void FileRecordNamePool_SameStringValue_ReturnsInternedInstance()
    {
        var pool = new FileRecordNamePool();
        var a = new string("shared".ToCharArray());
        var b = new string("shared".ToCharArray());
        Assert.IsFalse(ReferenceEquals(a, b)); // sanity: distinct instances before pooling

        var pooledA = pool.Get(a);
        var pooledB = pool.Get(b);

        Assert.IsTrue(ReferenceEquals(pooledA, pooledB));
    }

    [TestMethod]
    public void FileRecordNamePool_EmptyString_ReturnsEmptyWithoutThrowing()
    {
        var pool = new FileRecordNamePool();

        Assert.AreEqual(string.Empty, pool.Get(""));
    }

    [TestMethod]
    public void FileRecordNamePool_DifferentValues_AreKeptSeparate()
    {
        var pool = new FileRecordNamePool();

        Assert.AreEqual("a", pool.Get("a"));
        Assert.AreEqual("b", pool.Get("b"));
    }
}
