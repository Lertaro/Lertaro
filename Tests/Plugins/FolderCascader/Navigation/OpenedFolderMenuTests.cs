using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.Plugins.FolderCascader.Navigation;

namespace Lertaro.Plugins.FolderCascader.Tests.Navigation;

[TestClass]
public sealed class OpenedFolderMenuTests
{
    [TestMethod]
    public void GetUniqueOpenedFolderPaths_CollapsesCaseAndTrailingSeparatorDuplicates()
    {
        var folders = new[]
        {
            new OpenedFolder(@"C:\Projects", new IntPtr(1)),
            new OpenedFolder(@"c:\projects\", new IntPtr(2)),
            new OpenedFolder(@"D:\", new IntPtr(3)),
            new OpenedFolder(@"d:\", new IntPtr(4))
        };

        var paths = MenuBuilderContentExtensions.GetUniqueOpenedFolderPaths(folders);

        CollectionAssert.AreEqual(new[] { @"C:\Projects", @"D:\" }, paths);
    }
}
