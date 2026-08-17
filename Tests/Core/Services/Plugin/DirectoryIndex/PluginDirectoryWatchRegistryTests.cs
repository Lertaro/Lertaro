using Lertaro.Core.Services.Plugin.DirectoryIndex;

namespace Lertaro.Core.Tests.Services.Plugin.DirectoryIndex;

[TestClass]
public sealed class PluginDirectoryWatchRegistryTests
{
    [TestMethod]
    public void NormalizeDirectoryPath_UsesOneRegistrationKeyForTrailingSeparatorVariants()
    {
        var withoutSeparator = PluginDirectoryWatchRegistry.NormalizeDirectoryPath(@"C:\Apps");
        var withSeparator = PluginDirectoryWatchRegistry.NormalizeDirectoryPath(@"c:\apps\");

        Assert.AreEqual(withoutSeparator, withSeparator, ignoreCase: true);
    }

    [TestMethod]
    public void NormalizeDirectoryPath_PreservesDriveRoots()
        => Assert.AreEqual(@"C:\", PluginDirectoryWatchRegistry.NormalizeDirectoryPath(@"C:\"));
}
