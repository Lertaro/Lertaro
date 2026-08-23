using System.Windows.Controls;
using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowPluginHostTests
{
    [TestMethod]
    public void ConstructFlowPluginSettingsHostWindow_DoesNotThrow()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var metadata = new PluginMetadata
                {
                    ID = "TEST_ID",
                    Name = "TestPlugin"
                };
                var pair = new PluginPair { Metadata = metadata };
                var storage = new FlowSettingsStorage(Path.GetTempPath());
                var panel = new UserControl();

                var win = new FlowPluginSettingsHostWindow(pair, storage, panel);
                Assert.IsNotNull(win);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null)
        {
            Console.WriteLine($"ERROR: {error}");
            throw error;
        }
    }
}
