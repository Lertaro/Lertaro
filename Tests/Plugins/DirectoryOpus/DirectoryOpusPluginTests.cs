namespace Lertaro.Plugins.DirectoryOpus.Tests;

[TestClass]
public sealed class DirectoryOpusPluginTests
{
    [TestMethod]
    public void GetConfigSchema_EnablesSizeColumnByDefault()
    {
        var schema = new DirectoryOpusPlugin().GetConfigSchema();
        var field = schema.Fields.Single(item => item.Key == "EnableSizeColumn");

        Assert.IsTrue((bool)field.DefaultValue!);
    }
}
