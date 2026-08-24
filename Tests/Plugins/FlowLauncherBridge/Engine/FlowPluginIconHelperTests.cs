using Flow.Launcher.Plugin;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowPluginIconHelperTests
{
    [TestMethod]
    public void ResolvePluginIconPath_NullMeta_ReturnsNull() => Assert.IsNull(FlowPluginIconHelper.ResolvePluginIconPath(null));

    [TestMethod]
    public void ResolvePluginIconPath_EmptyIcoPath_ReturnsNull()
    {
        var meta = new PluginMetadata { IcoPath = "" };
        Assert.IsNull(FlowPluginIconHelper.ResolvePluginIconPath(meta));
    }

    [TestMethod]
    public void ResolvePluginIconPath_RelativePath_CombinesWithPluginDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "flow_icon_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var iconFile = Path.Combine(tempDir, "icon.png");
            File.WriteAllText(iconFile, "dummy");

            var meta = new PluginMetadata
            {
                PluginDirectory = tempDir,
                IcoPath = "icon.png"
            };

            var resolved = FlowPluginIconHelper.ResolvePluginIconPath(meta);
            Assert.AreEqual(iconFile, resolved);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void AttachPluginIcon_ExistingIcon_SetsIconDataOrHBitmap()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "flow_icon_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var iconFile = Path.Combine(tempDir, "icon.png");
            File.WriteAllText(iconFile, "dummy");

            var meta = new PluginMetadata
            {
                PluginDirectory = tempDir,
                IcoPath = "icon.png"
            };

            var item = new InstantResultItem { Title = "Test" };
            FlowPluginIconHelper.AttachPluginIcon(item, meta);

            Assert.IsTrue(item.HBitmapIcon != IntPtr.Zero || item.IconData == "path:" + iconFile);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
