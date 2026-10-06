using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowSettingsTransferTests
{
    private readonly string _root = Directory.CreateTempSubdirectory("FlowSettingsTransfer-").FullName;
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);

    public sealed class Config { public string Value { get; set; } = "initial"; }

    [TestMethod]
    public async Task PrepareForTransfer_FlushesSaveAndDisposeValues_BeforeUnloading()
    {
        var storage = new FlowSettingsStorage(_root);
        var config = storage.LoadSetting<Config>("中文_Name");
        var host = new FlowPluginHost(storage, []);
        host.RegisterPlugin(new PluginPair
        {
            Metadata = new PluginMetadata { ID = "stable-id", Name = "中文_Name", Disabled = true },
            Plugin = new SavingPlugin(() => config.Value = "saved", () => config.Value = "disposed")
        });

        await host.PrepareForSettingsTransferAsync();

        Assert.IsEmpty(host.GetAllPlugins());
        Assert.AreEqual("disposed", new FlowSettingsStorage(_root).LoadSetting<Config>("中文_Name").Value);
    }

    [TestMethod]
    public async Task PrepareForTransfer_SaveFailure_IsReportedAndKeepsPluginLoaded()
    {
        var storage = new FlowSettingsStorage(_root);
        var config = storage.LoadSetting<Config>("plugin");
        storage.SaveAll();
        var host = new FlowPluginHost(storage, []);
        var disposed = false;
        host.RegisterPlugin(new PluginPair
        {
            Metadata = new PluginMetadata { ID = "stable-id", Name = "plugin" },
            Plugin = new SavingPlugin(() => throw new IOException("save failed"), () => disposed = true)
        });
        config.Value = "unsaved";

        await Assert.ThrowsExactlyAsync<IOException>(() => host.PrepareForSettingsTransferAsync().AsTask());

        Assert.IsFalse(disposed);
        Assert.HasCount(1, host.GetAllPlugins());
        Assert.AreEqual("initial", new FlowSettingsStorage(_root).LoadSetting<Config>("plugin").Value);
    }

    [TestMethod]
    public async Task PrepareForTransfer_DisposeFailure_IsNotSilentlyAccepted()
    {
        var host = new FlowPluginHost(new FlowSettingsStorage(_root), []);
        host.RegisterPlugin(new PluginPair
        {
            Metadata = new PluginMetadata { ID = "stable-id" },
            Plugin = new SavingPlugin(() => { }, () => throw new IOException("writer still running"))
        });
        await Assert.ThrowsExactlyAsync<IOException>(() => host.PrepareForSettingsTransferAsync().AsTask());
        Assert.HasCount(1, host.GetAllPlugins());
    }

    private sealed class SavingPlugin(Action save, Action dispose) : IAsyncPlugin, ISavable, IDisposable
    {
        public Task InitAsync(PluginInitContext context) => Task.CompletedTask;
        public Task<List<Result>> QueryAsync(Query query, CancellationToken token) => Task.FromResult(new List<Result>());
        public void Save() => save();
        public void Dispose() => dispose();
    }
}
