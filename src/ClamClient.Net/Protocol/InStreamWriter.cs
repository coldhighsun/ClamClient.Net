using ClamClient.Net.Exceptions;
using System.Buffers;
using System.Buffers.Binary;

namespace ClamClient.Net.Protocol;

/// <summary>
/// Handles writing a stream to the ClamAV server using the INSTREAM command's chunked framing protocol.
/// </summary>
internal static class InStreamWriter
{
    /// <summary>
    /// Size in bytes of the big-endian length prefix that precedes every chunk.
    /// </summary>
    private const int LengthPrefixSize = 4;

    /// <summary>
    /// The zero-length chunk (four zero bytes) that signals the end of the stream to clamd.
    /// </summary>
    private static readonly byte[] Terminator = new byte[LengthPrefixSize];

    /// <summary>
    /// Writes <paramref name="source"/> to <paramref name="destination"/> using the INSTREAM framing protocol:
    /// each chunk is prefixed with its 4-byte big-endian length, terminated by a 4-byte zero.
    /// Throws <see cref="ClamStreamSizeExceededException"/> if the stream exceeds <paramref name="maxStreamSize"/>;
    /// the caller is responsible for marking the connection unhealthy in that case.
    /// </summary>
    internal static async Task WriteAsync(
        Stream source,
        Stream destination,
        int chunkSize,
        long maxStreamSize,
        CancellationToken cancellationToken)
    {
        // Rented rather than allocated: a default-sized chunk buffer (128 KB) would land on the large object heap on every scan.
        // The first 4 bytes of the buffer hold the chunk length so length prefix and data go out in one write.
        var buffer = ArrayPool<byte>.Shared.Rent(LengthPrefixSize + chunkSize);
        try
        {
            long totalBytes = 0;

            // The rented array may be larger than requested, so the read is limited to exactly chunkSize bytes.
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer.AsMemory(LengthPrefixSize, chunkSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > maxStreamSize)
                {
                    throw new ClamStreamSizeExceededException(maxStreamSize);
                }

                BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)bytesRead);
                await destination.WriteAsync(buffer.AsMemory(0, LengthPrefixSize + bytesRead), cancellationToken).ConfigureAwait(false);
            }

            // Zero-length chunk signals end of stream to clamd
            await destination.WriteAsync(Terminator, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Cleared because the buffer holds scanned content that must not leak to other users of the shared pool.
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}