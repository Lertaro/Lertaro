using System.Text;
using Lertaro.Core.Services.LocalSend;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class ProgressiveStreamContentTests
{
    [TestMethod]
    public async Task CopyToAsync_CancellationInterruptsPendingSourceRead()
    {
        using var source = new WaitingStream();
        using var content = new ProgressiveStreamContent(source, (_, _) => Assert.Fail("No bytes were read."));
        using var cancellation = new CancellationTokenSource();
        var copying = content.CopyToAsync(Stream.Null, cancellation.Token);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => copying.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private sealed class WaitingStream : MemoryStream
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    [TestMethod]
    public async Task ReadAsStreamAsync_ReportsProgress()
    {
        var testData = Encoding.UTF8.GetBytes("Hello Progressive Stream Content");
        using var ms = new MemoryStream(testData);

        long lastRead = 0;
        long totalLength = 0;
        var content = new ProgressiveStreamContent(ms, (read, total) =>
        {
            lastRead = read;
            totalLength = total;
        });

        using var outMs = new MemoryStream();
        await content.CopyToAsync(outMs);

        Assert.AreEqual(testData.Length, lastRead);
        Assert.AreEqual(testData.Length, totalLength);
        Assert.AreEqual(Encoding.UTF8.GetString(testData), Encoding.UTF8.GetString(outMs.ToArray()));
    }

    [TestMethod]
    public async Task CopyToAsync_EmptyStream_ReportsCompletion()
    {
        using var source = new MemoryStream();
        var reported = false;
        using var content = new ProgressiveStreamContent(source, (read, total) => reported = read == 0 && total == 0);

        await content.CopyToAsync(Stream.Null);

        Assert.IsTrue(reported);
    }
}
