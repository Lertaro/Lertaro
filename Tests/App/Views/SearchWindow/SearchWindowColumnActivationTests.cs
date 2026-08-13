using Lertaro.App.Views.SearchWindow;
using System.IO;

namespace Lertaro.App.Tests.Views.SearchWindow;

[TestClass]
public sealed class SearchWindowColumnActivationTests
{
    [TestMethod]
    public void IsFileOrFolder_UnknownKind_OnlyChecksDeclaredFilesystemKind()
    {
        var directoryAsFile = new AppSearchResult
        {
            FullPath = Path.GetTempPath(),
            IsDir = false,
            ResultKind = "InstantResult"
        };

        Assert.IsFalse(SearchWindowColumnActivation.IsFileOrFolder(directoryAsFile));
    }

    [TestMethod]
    public void IsFileOrFolder_KnownIndexedKind_DoesNotRequireAReachablePath()
    {
        var indexed = new AppSearchResult
        {
            FullPath = @"Z:\unreachable\indexed-item",
            IsDir = true,
            ResultKind = "File"
        };

        Assert.IsTrue(SearchWindowColumnActivation.IsFileOrFolder(indexed));
    }
}
