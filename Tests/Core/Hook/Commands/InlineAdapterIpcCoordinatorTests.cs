using Lertaro.Core.Hook.Commands;
using Lertaro.Core.Wire;

namespace Lertaro.Core.Tests.Hook.Commands;

// ExecuteItem's tests all resolve the reply SYNCHRONOUSLY from within the sendMsg callback (which runs
// before evt.WaitOne is reached), so none of them touch the real 1s/4s timeout paths -- calling
// SetExecuteItemResult from any other thread/timing would race other tests via the class's shared static
// fields, but ExecuteItem's own lock serializes callers, so resolving inline while still holding that lock
// is race-free.
[TestClass]
public sealed class InlineAdapterIpcCoordinatorTests
{
    [TestMethod]
    public void ExecuteItem_ReplyResolvesTrueSynchronously_ReturnsTrueWithoutWaiting()
    {
        var result = InlineAdapterIpcCoordinator.ExecuteItem(
            IntPtr.Zero, @"C:\file.txt", isDir: false, "query",
            msg => InlineAdapterIpcCoordinator.SetExecuteItemResult((int)msg.IntVal, true),
            out var lateResult);

        Assert.IsTrue(result);
        Assert.IsTrue(lateResult.IsCompleted);
        Assert.IsTrue(lateResult.Result);
    }

    [TestMethod]
    public void ExecuteItem_ReplyResolvesFalseSynchronously_ReturnsFalse()
    {
        var result = InlineAdapterIpcCoordinator.ExecuteItem(
            IntPtr.Zero, @"C:\file.txt", isDir: false, "query",
            msg => InlineAdapterIpcCoordinator.SetExecuteItemResult((int)msg.IntVal, false),
            out var lateResult);

        Assert.IsFalse(result);
        Assert.IsTrue(lateResult.IsCompleted);
        Assert.IsFalse(lateResult.Result);
    }

    [TestMethod]
    public void ExecuteItem_DirectoryPathMissingSeparator_NormalizesWithTrailingBackslash()
    {
        IpcMessage? sent = null;
        InlineAdapterIpcCoordinator.ExecuteItem(
            IntPtr.Zero, @"C:\folder", isDir: true, "query",
            msg => { sent = msg; InlineAdapterIpcCoordinator.SetExecuteItemResult((int)msg.IntVal, true); },
            out _);

        Assert.AreEqual(@"C:\folder\", sent!.Value.StringVal1);
    }

    [TestMethod]
    public void ExecuteItem_FilePathWithTrailingSeparator_IsTrimmed()
    {
        IpcMessage? sent = null;
        InlineAdapterIpcCoordinator.ExecuteItem(
            IntPtr.Zero, @"C:\file.txt\", isDir: false, "query",
            msg => { sent = msg; InlineAdapterIpcCoordinator.SetExecuteItemResult((int)msg.IntVal, true); },
            out _);

        Assert.AreEqual(@"C:\file.txt", sent!.Value.StringVal1);
    }

    [TestMethod]
    public void ExecuteItem_SendsExpectedMessageShape()
    {
        IpcMessage? sent = null;
        var hwnd = new IntPtr(12345);
        InlineAdapterIpcCoordinator.ExecuteItem(
            hwnd, @"C:\file.txt", isDir: false, "my query",
            msg => { sent = msg; InlineAdapterIpcCoordinator.SetExecuteItemResult((int)msg.IntVal, true); },
            out _);

        Assert.AreEqual(IpcMessageId.ExecuteInlineItem, sent!.Value.Id);
        Assert.AreEqual(hwnd.ToInt64(), sent.Value.Hwnd);
        Assert.AreEqual("my query", sent.Value.StringVal2);
    }

    [TestMethod]
    public async Task RunAfterLateResultAsync_TrueResult_InvokesOnSuccessOnly()
    {
        var successCalled = false;
        var fallbackCalled = false;

        await InlineAdapterIpcCoordinator.RunAfterLateResultAsync(Task.FromResult(true), () => successCalled = true, () => fallbackCalled = true);

        Assert.IsTrue(successCalled);
        Assert.IsFalse(fallbackCalled);
    }

    [TestMethod]
    public async Task RunAfterLateResultAsync_FalseResult_InvokesOnFallbackOnly()
    {
        var successCalled = false;
        var fallbackCalled = false;

        await InlineAdapterIpcCoordinator.RunAfterLateResultAsync(Task.FromResult(false), () => successCalled = true, () => fallbackCalled = true);

        Assert.IsFalse(successCalled);
        Assert.IsTrue(fallbackCalled);
    }
}
