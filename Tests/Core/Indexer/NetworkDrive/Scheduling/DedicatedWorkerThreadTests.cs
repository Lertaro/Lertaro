using Lertaro.Core.Indexer.NetworkDrive.Scheduling;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Scheduling;

[TestClass]
public sealed class DedicatedWorkerThreadTests
{
    [TestMethod]
    public async Task Run_WorkCompletesSuccessfully_TaskCompletes()
    {
        var ran = false;
        await DedicatedWorkerThread.Run(() => { ran = true; return Task.CompletedTask; }, "test");

        Assert.IsTrue(ran);
    }

    [TestMethod]
    public async Task Run_WorkThrowsOperationCanceled_TaskIsCanceledNotFaulted()
    {
        var task = DedicatedWorkerThread.Run(() => throw new OperationCanceledException(), "test");

        try
        {
            await task;
            Assert.Fail("Expected a canceled task.");
        }
        catch (OperationCanceledException)
        {
        }

        Assert.AreEqual(TaskStatus.Canceled, task.Status);
    }

    [TestMethod]
    public async Task Run_WorkThrowsRegularException_TaskFaultsWithSameException()
    {
        var task = DedicatedWorkerThread.Run(() => throw new InvalidOperationException("boom"), "test");

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => task);

        Assert.AreEqual("boom", thrown.Message);
    }

    [TestMethod]
    public async Task Run_ReturnsResultOfAwaitingInnerTask()
    {
        var result = 0;
        await DedicatedWorkerThread.Run(async () =>
        {
            await Task.Delay(1);
            result = 42;
        }, "test");

        Assert.AreEqual(42, result);
    }
}
