using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Search;

[TestClass]
public sealed class ScopeLiveSearchCacheTests
{
    [TestMethod]
    public void GetOrAdd_SameDirectory_ProbesOnlyOnce()
    {
        var cache = new ScopeLiveSearchCache();
        var calls = 0;

        var first = cache.GetOrAdd(@"C:\Projects", _ =>
        {
            calls++;
            return true;
        });
        var second = cache.GetOrAdd(@"c:\projects", _ =>
        {
            calls++;
            return false;
        });

        Assert.IsTrue(first);
        Assert.IsTrue(second);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void GetOrAdd_DifferentDirectories_ProbesEachDirectory()
    {
        var cache = new ScopeLiveSearchCache();
        var calls = 0;

        Assert.IsFalse(cache.GetOrAdd(@"C:\Projects", _ =>
        {
            calls++;
            return false;
        }));
        Assert.IsTrue(cache.GetOrAdd(@"C:\Downloads", _ =>
        {
            calls++;
            return true;
        }));

        Assert.AreEqual(2, calls);
    }
}
