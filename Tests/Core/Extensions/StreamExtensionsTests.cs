using Lertaro.Core.Extensions;

namespace Lertaro.Core.Tests.Extensions;

[TestClass]
public sealed class StreamExtensionsTests
{
    [TestMethod]
    public async Task ReadExactlyAsync_ReadsRequestedByteCount()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });

        var result = await stream.ReadExactlyAsync(3, CancellationToken.None);

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, result);
    }

    [TestMethod]
    public async Task ReadExactlyAsync_StreamEndsEarly_ThrowsEndOfStreamException()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2 });

        await Assert.ThrowsExactlyAsync<EndOfStreamException>(
            () => stream.ReadExactlyAsync(5, CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadExactlyAsync_ZeroCount_ReturnsEmptyArray()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await stream.ReadExactlyAsync(0, CancellationToken.None);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task ReadInt32Async_RoundTripsLittleEndianValue()
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(123456));

        var result = await stream.ReadInt32Async(CancellationToken.None);

        Assert.AreEqual(123456, result);
    }

    [TestMethod]
    public async Task ReadInt32Async_NegativeValue_RoundTrips()
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(-42));

        var result = await stream.ReadInt32Async(CancellationToken.None);

        Assert.AreEqual(-42, result);
    }

    [TestMethod]
    public async Task ReadExactlyAsync_ReadAcrossMultipleUnderlyingReads_StillAssemblesFullBuffer()
    {
        // A stream that only ever returns 1 byte per ReadAsync call, to exercise the retry loop.
        using var slow = new OneByteAtATimeStream(new byte[] { 10, 20, 30, 40 });

        var result = await slow.ReadExactlyAsync(4, CancellationToken.None);

        CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 40 }, result);
    }

    private sealed class OneByteAtATimeStream : MemoryStream
    {
        public OneByteAtATimeStream(byte[] buffer) : base(buffer) { }

        public override int Read(byte[] buffer, int offset, int count)
            => base.Read(buffer, offset, Math.Min(1, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }
}
