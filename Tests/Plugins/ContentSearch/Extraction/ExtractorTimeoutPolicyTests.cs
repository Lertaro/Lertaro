using Lertaro.Plugins.ContentSearch.Extraction;

namespace Lertaro.Plugins.ContentSearch.Tests.Extraction;

[TestClass]
public sealed class ExtractorTimeoutPolicyTests
{
    [TestMethod]
    public void ForFileSize_SmallFile_FloorsAtFiveSeconds()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(5), ExtractorTimeoutPolicy.ForFileSize(0));
        Assert.AreEqual(TimeSpan.FromSeconds(5), ExtractorTimeoutPolicy.ForFileSize(512 * 1024));
    }

    [TestMethod]
    public void ForFileSize_MidSizedFile_ScalesAtFiveSecondsPerMegabyte()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(10), ExtractorTimeoutPolicy.ForFileSize(2 * 1024 * 1024));
        Assert.AreEqual(TimeSpan.FromSeconds(25), ExtractorTimeoutPolicy.ForFileSize(5 * 1024 * 1024));
    }

    [TestMethod]
    public void ForFileSize_HugeFile_CapsAtTwoMinutes()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(120), ExtractorTimeoutPolicy.ForFileSize(100 * 1024 * 1024));
        Assert.AreEqual(TimeSpan.FromSeconds(120), ExtractorTimeoutPolicy.ForFileSize(long.MaxValue));
    }
}
