using System.IO.Pipes;
using System.Reflection;
using Lertaro.Core.Hook.Ipc;
using Lertaro.Core.Wire;

namespace Lertaro.Core.Tests.Services.Pipe;

[TestClass]
public sealed class HookShutdownTests
{
    [TestMethod]
    public async Task StopAsync_WritesStopBeforeClosingAndDoesNotRestart()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var name = "LertaroHookStop_" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        using var hook = new HookIpcClient();
        typeof(HookIpcClient).GetField("_cmdPipe", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(hook, client);
        var stopping = hook.StopAsync();
        var message = await PipeRequestBinarySerializer.ReadMessageAsync(server, timeout.Token);
        Assert.AreEqual(IpcMessageId.Stop, message.Id);
        await stopping.WaitAsync(timeout.Token);
        Assert.AreEqual(0, await server.ReadAsync(new byte[1], timeout.Token));
        Assert.AreSame(stopping, hook.StopAsync());
        hook.Start();
        Assert.IsNull(typeof(HookIpcClient).GetField("_listenTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hook));
    }
}
