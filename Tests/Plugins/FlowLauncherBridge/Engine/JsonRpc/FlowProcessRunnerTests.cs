using System.Text.Json;
using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.JsonRpc;

[TestClass]
public sealed class FlowProcessRunnerTests
{
    [TestMethod]
    public void JsonRpcResponse_Deserialization_Works()
    {
        const string json = "{\"result\":[{\"Title\":\"Hello\",\"SubTitle\":\"World\",\"IcoPath\":\"Images/app.png\",\"JsonRPCAction\":{\"method\":\"flow_open_url\",\"parameters\":[\"https://google.com\"]}}]}";
        var response = JsonSerializer.Deserialize<JsonRpcResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(response);
        Assert.IsNotNull(response.Result);
        Assert.HasCount(1, response.Result);
        Assert.AreEqual("Hello", response.Result[0].Title);
        Assert.AreEqual("World", response.Result[0].SubTitle);
        var action = response.Result[0].JsonRPCAction;
        Assert.IsNotNull(action);
        Assert.AreEqual("flow_open_url", action.Method);
    }

    [TestMethod]
    public void FlowJsonRpcPlugin_Constructs_AndInitializes()
    {
        var meta = new PluginMetadata { ID = "TEST_RPC", Name = "RpcPlugin" };
        var runner = new FlowProcessRunner(meta, "non_existent_binary.exe");
        var plugin = new FlowJsonRpcPlugin(runner);
        Assert.IsNotNull(plugin);
    }
}
