namespace Lertaro.Core.Tests;

[TestClass]
public sealed class FileSystemItemFilterTests
{
    [TestMethod]
    public void IsHiddenOrSystem_HiddenAttribute_ReturnsTrue() => Assert.IsTrue(FileSystemItemFilter.IsHiddenOrSystem(FileAttributes.Hidden));

    [TestMethod]
    public void IsHiddenOrSystem_SystemAttribute_ReturnsTrue() => Assert.IsTrue(FileSystemItemFilter.IsHiddenOrSystem(FileAttributes.System));

    [TestMethod]
    public void IsHiddenOrSystem_NormalAttribute_ReturnsFalse() => Assert.IsFalse(FileSystemItemFilter.IsHiddenOrSystem(FileAttributes.Normal));

    [TestMethod]
    public void IsHiddenOrSystem_ReadOnlyAttribute_ReturnsFalse() => Assert.IsFalse(FileSystemItemFilter.IsHiddenOrSystem(FileAttributes.ReadOnly));

    [TestMethod]
    public void IsHiddenOrSystem_CombinedWithOtherFlags_StillDetectsHidden()
    {
        var combined = FileAttributes.Hidden | FileAttributes.Archive;

        Assert.IsTrue(FileSystemItemFilter.IsHiddenOrSystem(combined));
    }

    [TestMethod]
    public void IsHiddenOrSystem_NullSearchResult_ReturnsFalse() => Assert.IsFalse(FileSystemItemFilter.IsHiddenOrSystem((SearchResult)null!));

    [TestMethod]
    public void IsHiddenOrSystem_SearchResultUsesCachedAttributes()
    {
        var result = new SearchResult { Attributes = FileAttributes.Hidden };

        Assert.IsTrue(FileSystemItemFilter.IsHiddenOrSystem(result));
    }

    [TestMethod]
    public void IsHiddenOrSystem_EmptyPath_ReturnsFalse() => Assert.IsFalse(FileSystemItemFilter.IsHiddenOrSystem(""));

    [TestMethod]
    public void IsHiddenOrSystem_NonExistentPath_ReturnsFalseInsteadOfThrowing() => Assert.IsFalse(FileSystemItemFilter.IsHiddenOrSystem(@"z:\this\path\does\not\exist\at\all"));
}
