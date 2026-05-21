using System.Buffers.Binary;
using ClamClient.Net.Exceptions;
using ClamClient.Net.Protocol;

namespace ClamClient.Net.Tests.Protocol;

public sealed class InStreamWriterTests
{
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
            if (len == 0) break;
            reconstructed.AddRange(written[(pos + 4)..(pos + 4 + len)]);
            pos += 4 + len;
        }

        Assert.Equal(payload, reconstructed.ToArray());
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
}
