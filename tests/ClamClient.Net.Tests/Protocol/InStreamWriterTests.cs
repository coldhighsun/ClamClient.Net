using ClamClient.Net.Exceptions;
using ClamClient.Net.Protocol;
using System.Buffers.Binary;

namespace ClamClient.Net.Tests.Protocol;

public sealed class InStreamWriterTests
{
    [Fact]
    public async Task WriteAsync_EmptyStream_WritesOnlyTerminator()
    {
        var source = new MemoryStream();
        var destination = new MemoryStream();

        await InStreamWriter.WriteAsync(source, destination, chunkSize: 1024, maxStreamSize: 1024 * 1024, TestContext.Current.CancellationToken);

        var written = destination.ToArray();
        Assert.Equal(4, written.Length);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(0, 4)));
    }

    [Fact]
    public async Task WriteAsync_ExceedsMaxStreamSize_ThrowsClamStreamSizeExceededException()
    {
        var payload = new byte[200];
        var source = new MemoryStream(payload);
        var destination = new MemoryStream();

        await Assert.ThrowsAsync<ClamStreamSizeExceededException>(
            () => InStreamWriter.WriteAsync(source, destination, chunkSize: 1024, maxStreamSize: 100, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteAsync_MultipleChunks_WritesAllChunks()
    {
        var payload = new byte[300];
        Random.Shared.NextBytes(payload);
        var source = new MemoryStream(payload);
        var destination = new MemoryStream();

        await InStreamWriter.WriteAsync(source, destination, chunkSize: 100, maxStreamSize: 1024 * 1024, TestContext.Current.CancellationToken);

        var written = destination.ToArray();

        var reconstructed = new List<byte>();
        var pos = 0;
        while (pos < written.Length - 4)
        {
            var len = (int)BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(pos, 4));
            if (len == 0)
                break;
            reconstructed.AddRange(written[(pos + 4)..(pos + 4 + len)]);
            pos += 4 + len;
        }

        Assert.Equal(payload, reconstructed.ToArray());
    }

    /// <summary>
    /// Verifies that the length prefix and the chunk data are written in one call, followed by one call for the terminator.
    /// </summary>
    [Fact]
    public async Task WriteAsync_SinglePayloadChunk_WritesLengthAndDataInOneCall()
    {
        var source = new MemoryStream("Hello, ClamAV!"u8.ToArray());
        var destination = new WriteCountingStream();

        await InStreamWriter.WriteAsync(source, destination, chunkSize: 1024, maxStreamSize: 1024 * 1024, TestContext.Current.CancellationToken);

        // One write for length prefix + data, one for the terminator.
        Assert.Equal(2, destination.WriteCount);
    }

    /// <summary>
    /// Verifies that each chunk is written with its length prefix in a single call, plus one call for the terminator.
    /// </summary>
    [Fact]
    public async Task WriteAsync_MultipleChunks_WritesOneCallPerChunkPlusTerminator()
    {
        var source = new MemoryStream(new byte[250]);
        var destination = new WriteCountingStream();

        await InStreamWriter.WriteAsync(source, destination, chunkSize: 100, maxStreamSize: 1024 * 1024, TestContext.Current.CancellationToken);

        // 100 + 100 + 50 bytes = 3 chunk writes, then the terminator.
        Assert.Equal(4, destination.WriteCount);
    }

    /// <summary>
    /// A write-only stream that counts how many write calls it receives.
    /// </summary>
    private sealed class WriteCountingStream : MemoryStream
    {
        /// <summary>
        /// Gets the number of write calls received.
        /// </summary>
        public int WriteCount { get; private set; }

        /// <inheritdoc/>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteCount++;
            return base.WriteAsync(buffer, cancellationToken);
        }
    }

    [Fact]
    public async Task WriteAsync_SmallPayload_WritesCorrectFraming()
    {
        var payload = "Hello, ClamAV!"u8.ToArray();
        var source = new MemoryStream(payload);
        var destination = new MemoryStream();

        await InStreamWriter.WriteAsync(source, destination, chunkSize: 1024, maxStreamSize: 1024 * 1024, TestContext.Current.CancellationToken);

        var written = destination.ToArray();

        var chunkLen = BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(0, 4));
        Assert.Equal((uint)payload.Length, chunkLen);

        var data = written[4..(4 + payload.Length)];
        Assert.Equal(payload, data);

        var terminator = BinaryPrimitives.ReadUInt32BigEndian(written.AsSpan(written.Length - 4, 4));
        Assert.Equal(0u, terminator);
    }
}