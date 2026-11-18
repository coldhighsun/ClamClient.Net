using System.Buffers;

namespace ClamClient.Net.Pool;

/// <summary>
/// A write-only stream that buffers small writes and sends them to the underlying stream as a single write on flush.
/// </summary>
/// <remarks>
/// clamd stalls for roughly 50 ms on a reused IDSESSION connection when an INSTREAM request arrives split across
/// several TCP segments (command, data, terminator). Coalescing the request into one write avoids that stall.
/// The buffer is rented from <see cref="ArrayPool{T}.Shared"/> and must be returned by disposing the stream.
/// </remarks>
internal sealed class CoalescingWriteStream : Stream
{
    /// <summary>
    /// Capacity in bytes of the rented buffer. It is large enough to hold a command, one default-sized INSTREAM chunk
    /// and the terminator; anything bigger is sent early or forwarded directly.
    /// </summary>
    internal const int MaxBufferedBytes = 256 * 1024;

    /// <summary>
    /// The stream that receives the coalesced writes.
    /// </summary>
    private readonly Stream _inner;

    /// <summary>
    /// The pooled buffer that accumulates bytes that have been written but not yet sent; <see langword="null"/> once disposed.
    /// </summary>
    private byte[]? _buffer;

    /// <summary>
    /// The number of bytes currently held in <see cref="_buffer"/>.
    /// </summary>
    private int _count;

    /// <summary>
    /// Initializes a new instance that forwards coalesced writes to <paramref name="inner"/>.
    /// </summary>
    /// <param name="inner">The stream that receives the coalesced writes.</param>
    internal CoalescingWriteStream(Stream inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _buffer = ArrayPool<byte>.Shared.Rent(MaxBufferedBytes);
    }

    /// <inheritdoc/>
    public override bool CanRead => false;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => true;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var pooled = _buffer ?? throw new ObjectDisposedException(nameof(CoalescingWriteStream));

        if (_count + buffer.Length > MaxBufferedBytes)
        {
            await SendBufferedAsync(cancellationToken).ConfigureAwait(false);
        }

        if (buffer.Length > MaxBufferedBytes)
        {
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            return;
        }

        buffer.CopyTo(pooled.AsMemory(_count));
        _count += buffer.Length;
    }

    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await SendBufferedAsync(cancellationToken).ConfigureAwait(false);
        await _inner.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override void Flush() => throw new NotSupportedException("Use FlushAsync.");

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use WriteAsync.");

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && _buffer is { } pooled)
        {
            _buffer = null;
            _count = 0;
            // Cleared because the buffer holds scanned content that must not leak to other users of the shared pool.
            ArrayPool<byte>.Shared.Return(pooled, clearArray: true);
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Sends any buffered bytes to the underlying stream in one write and clears the buffer.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the buffered bytes have been written.</returns>
    private async ValueTask SendBufferedAsync(CancellationToken cancellationToken)
    {
        if (_count == 0)
        {
            return;
        }

        var pooled = _buffer ?? throw new ObjectDisposedException(nameof(CoalescingWriteStream));
        var count = _count;
        _count = 0;
        await _inner.WriteAsync(pooled.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
    }
}
