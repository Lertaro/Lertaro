using System.IO;
using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services.InlineSearch;

[TestClass]
public sealed class ExplorerPathValidatorTests
{
    [TestMethod]
    public void IsUsableDirectory_AcceptsExistingAndWslDirectories()
    {
        Assert.IsTrue(ExplorerPathValidator.IsUsableDirectory(Path.GetTempPath()));
        Assert.IsTrue(ExplorerPathValidator.IsUsableDirectory(@"\\wsl$\Ubuntu\home"));
    }

    [TestMethod]
    public void IsUsableDirectory_RejectsMissingAndBlankPaths()
    {
        Assert.IsFalse(ExplorerPathValidator.IsUsableDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        Assert.IsFalse(ExplorerPathValidator.IsUsableDirectory("  "));
    }

    [TestMethod]
    public void FilterReportedDirectories_RemovesInvalidAndDuplicatePaths()
    {
        var paths = ExplorerPathValidator.FilterReportedDirectories(new[]
        {
            Path.GetTempPath(),
            Path.GetTempPath().ToUpperInvariant(),
            Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            @"\\wsl$\Ubuntu\home",
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        });

        Assert.HasCount(2, paths);
    }
}
