using Lertaro.Core.Hook.Ipc;
using Lertaro.Core.Wire;

namespace Lertaro.Core.Tests.Hook.Ipc;

// The queue is what a hook process accumulates while no App is reading it -- one message per keystroke,
// per mouse click, per captured path -- so its ceiling is the only thing standing between a long session
// and a working set made of every event that ever queued.
[TestClass]
public sealed class HookIpcServerSendQueueTests
{
    [TestMethod]
    public void TheSendQueue_StopsAtItsCapacityInsteadOfGrowing()
    {
        var channel = HookIpcServer.CreateSendChannel();

        for (var i = 0; i < HookIpcServer.SendQueueCapacity * 2; i++)
            channel.Writer.TryWrite(new IpcMessage { Id = IpcMessageId.KeyChar });

        Assert.IsTrue(channel.Reader.CanCount);
        Assert.AreEqual(HookIpcServer.SendQueueCapacity, channel.Reader.Count);
    }

    [TestMethod]
    public void TheSendQueue_DropsTheOldestEventsFirst()
    {
        var channel = HookIpcServer.CreateSendChannel();
        const int overflow = 5;

        for (var i = 0; i < HookIpcServer.SendQueueCapacity; i++)
            channel.Writer.TryWrite(new IpcMessage { Id = IpcMessageId.KeyChar });
        for (var i = 0; i < overflow; i++)
            channel.Writer.TryWrite(new IpcMessage { Id = IpcMessageId.MouseClick });

        var kept = new List<IpcMessageId>();
        while (channel.Reader.TryRead(out var msg))
            kept.Add(msg.Id);

        Assert.HasCount(HookIpcServer.SendQueueCapacity, kept);
        Assert.HasCount(overflow, kept.Where(id => id == IpcMessageId.MouseClick), "the newest events survived");
        Assert.HasCount(HookIpcServer.SendQueueCapacity - overflow, kept.Where(id => id == IpcMessageId.KeyChar),
            "what was dropped is the oldest, not the newest");
    }
}
