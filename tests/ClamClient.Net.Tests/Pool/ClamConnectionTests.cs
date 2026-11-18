using ClamClient.Net.Pool;
using ClamClient.Net.Protocol;

namespace ClamClient.Net.Tests.Pool;

/// <summary>
/// Unit tests for how <see cref="ClamConnection"/> writes requests to its underlying stream.
/// </summary>
public sealed class ClamConnectionTests
{
    /// <summary>
    /// Verifies that a small INSTREAM request (command, chunk and terminator) reaches the stream as a single write.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SmallInStream_WritesRequestInOneCall()
    {
        var ct = TestContext.Current.CancellationToken;
        var stream = new ScriptedStream("stream: OK\0"u8.ToArray());
        var connection = new ClamConnection(stream);
        var payload = "Hello, ClamAV!"u8.ToArray();

        var response = await connection.ExecuteAsync(
            ClamConnection.InStreamBytes,
            (destination, token) => InStreamWriter.WriteAsync(
                new MemoryStream(payload), destination, chunkSize: 1024, maxStreamSize: 1024 * 1024, token),
            ct);

        Assert.Contains("OK", response);
        var write = Assert.Single(stream.Writes);
        var expected = ClamConnection.InStreamBytes
            .Concat(new byte[] { 0, 0, 0, (byte)payload.Length })
            .Concat(payload)
            .Concat(new byte[] { 0, 0, 0, 0 })
            .ToArray();
        Assert.Equal(expected, write);
    }

    /// <summary>
    /// Verifies that a command without a payload is written directly in one call.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NoPayload_WritesCommandInOneCall()
    {
        var stream = new ScriptedStream("PONG\0"u8.ToArray());
        var connection = new ClamConnection(stream);

        await connection.ExecuteAsync(ClamConnection.PingBytes, afterCommand: null, TestContext.Current.CancellationToken);

        var write = Assert.Single(stream.Writes);
        Assert.Equal(ClamConnection.PingBytes, write);
    }

    /// <summary>
    /// A stream that records every write and serves a fixed response on read.
    /// </summary>
    private sealed class ScriptedStream(byte[] response) : Stream
    {
        /// <summary>
        /// The response bytes that remain to be read.
        /// </summary>
        private readonly MemoryStream _response = new(response);

        /// <summary>
        /// Gets the payloads of all write calls, in order.
        /// </summary>
        public List<byte[]> Writes { get; } = [];

        /// <inheritdoc/>
        public override bool CanRead => true;

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
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _response.ReadAsync(buffer, cancellationToken);

        /// <inheritdoc/>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Writes.Add(buffer.ToArray());
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <inheritdoc/>
        public override void Flush()
        {
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) => _response.Read(buffer, offset, count);

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
