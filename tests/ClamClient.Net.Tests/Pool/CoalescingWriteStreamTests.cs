using ClamClient.Net.Pool;

namespace ClamClient.Net.Tests.Pool;

/// <summary>
/// Unit tests for <see cref="CoalescingWriteStream"/>.
/// </summary>
public sealed class CoalescingWriteStreamTests
{
    /// <summary>
    /// Verifies that several small writes are sent to the inner stream as one write on flush.
    /// </summary>
    [Fact]
    public async Task FlushAsync_AfterSmallWrites_SendsOneInnerWrite()
    {
        var inner = new WriteRecordingStream();
        using var stream = new CoalescingWriteStream(inner);
        var ct = TestContext.Current.CancellationToken;

        await stream.WriteAsync("abc"u8.ToArray(), ct);
        await stream.WriteAsync("def"u8.ToArray(), ct);
        Assert.Empty(inner.Writes);

        await stream.FlushAsync(ct);

        var write = Assert.Single(inner.Writes);
        Assert.Equal("abcdef"u8.ToArray(), write);
    }

    /// <summary>
    /// Verifies that flushing with nothing buffered does not produce an inner write.
    /// </summary>
    [Fact]
    public async Task FlushAsync_NothingBuffered_SendsNoInnerWrite()
    {
        var inner = new WriteRecordingStream();
        using var stream = new CoalescingWriteStream(inner);

        await stream.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Empty(inner.Writes);
    }

    /// <summary>
    /// Verifies that a write which would overflow the buffer first sends the already-buffered bytes.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ExceedsBufferLimit_SendsBufferedBytesBeforeContinuing()
    {
        var inner = new WriteRecordingStream();
        using var stream = new CoalescingWriteStream(inner);
        var ct = TestContext.Current.CancellationToken;
        var nearlyFull = new byte[CoalescingWriteStream.MaxBufferedBytes - 1];

        await stream.WriteAsync(nearlyFull, ct);
        await stream.WriteAsync(new byte[] { 1, 2 }, ct);
        await stream.FlushAsync(ct);

        Assert.Equal(2, inner.Writes.Count);
        Assert.Equal(nearlyFull.Length, inner.Writes[0].Length);
        Assert.Equal(new byte[] { 1, 2 }, inner.Writes[1]);
    }

    /// <summary>
    /// Verifies that writes which exactly fill the buffer are still coalesced into a single inner write.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ExactlyFillsBuffer_StaysCoalesced()
    {
        var inner = new WriteRecordingStream();
        using var stream = new CoalescingWriteStream(inner);
        var ct = TestContext.Current.CancellationToken;

        await stream.WriteAsync(new byte[CoalescingWriteStream.MaxBufferedBytes - 1], ct);
        await stream.WriteAsync(new byte[] { 1 }, ct);
        await stream.FlushAsync(ct);

        var write = Assert.Single(inner.Writes);
        Assert.Equal(CoalescingWriteStream.MaxBufferedBytes, write.Length);
    }

    /// <summary>
    /// Verifies that a single write larger than the buffer is forwarded directly.
    /// </summary>
    [Fact]
    public async Task WriteAsync_SingleWriteLargerThanLimit_IsForwardedDirectly()
    {
        var inner = new WriteRecordingStream();
        using var stream = new CoalescingWriteStream(inner);
        var ct = TestContext.Current.CancellationToken;
        var big = new byte[CoalescingWriteStream.MaxBufferedBytes + 10];

        await stream.WriteAsync(big, ct);

        var write = Assert.Single(inner.Writes);
        Assert.Equal(big.Length, write.Length);
    }

    /// <summary>
    /// Verifies that the <c>byte[]</c> overload of <c>WriteAsync</c> is buffered like the <see cref="ReadOnlyMemory{T}"/> one.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ByteArrayOverload_IsBufferedAndHonoursOffset()
    {
        var inner = new WriteRecordingStream();
        using var stream = new CoalescingWriteStream(inner);
        var ct = TestContext.Current.CancellationToken;

        await stream.WriteAsync("xabcx"u8.ToArray(), 1, 3, ct);
        await stream.FlushAsync(ct);

        var write = Assert.Single(inner.Writes);
        Assert.Equal("abc"u8.ToArray(), write);
    }

    /// <summary>
    /// Verifies that writing after disposal throws <see cref="ObjectDisposedException"/>.
    /// </summary>
    [Fact]
    public async Task WriteAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var stream = new CoalescingWriteStream(new WriteRecordingStream());
        stream.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await stream.WriteAsync(new byte[] { 1 }, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that a null inner stream is rejected.
    /// </summary>
    [Fact]
    public void Constructor_NullInner_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CoalescingWriteStream(null!));
    }

    /// <summary>
    /// A stream that records the payload of every write call.
    /// </summary>
    private sealed class WriteRecordingStream : MemoryStream
    {
        /// <summary>
        /// Gets the payloads of all write calls, in order.
        /// </summary>
        public List<byte[]> Writes { get; } = [];

        /// <inheritdoc/>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Writes.Add(buffer.ToArray());
            return ValueTask.CompletedTask;
        }
    }
}
