using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.FolderCascader.Tests;

[TestClass]
public sealed class FolderCascaderPluginTests
{
    [TestMethod]
    public void GetConfigSchema_OpenedFoldersAreShownByDefault()
    {
        var fields = new FolderCascaderPlugin().GetConfigSchema().Fields.Single().SubFields!;

        var setting = fields.Single(field => field.Key == "ShowOpenedFolders");

        Assert.AreEqual(ConfigFieldType.Boolean, setting.FieldType);
        Assert.IsTrue((bool)setting.DefaultValue!);
    }
}
