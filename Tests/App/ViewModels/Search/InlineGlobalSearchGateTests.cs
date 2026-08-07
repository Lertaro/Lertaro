using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class InlineGlobalSearchGateTests
{
    [TestMethod]
    public async Task WaitForLocalSearchAsync_CompletedLocalSearch_ReturnsImmediately() => await InlineGlobalSearchGate.WaitForLocalSearchAsync(Task.CompletedTask, CancellationToken.None);

    [TestMethod]
    public async Task WaitForLocalSearchAsync_LocalSearchCompletesBeforeDelay_Returns()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = InlineGlobalSearchGate.WaitForLocalSearchAsync(completion.Task, CancellationToken.None);

        completion.SetResult();
        await wait;
    }

    [TestMethod]
    public async Task WaitForLocalSearchAsync_CancelledSearch_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            InlineGlobalSearchGate.WaitForLocalSearchAsync(new TaskCompletionSource().Task, cancellation.Token));
    }
}
