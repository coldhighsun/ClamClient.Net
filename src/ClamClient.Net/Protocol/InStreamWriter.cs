using ClamClient.Net.Exceptions;
using System.Buffers.Binary;

namespace ClamClient.Net.Protocol;

/// <summary>
/// Handles writing a stream to the ClamAV server using the INSTREAM command's chunked framing protocol.
/// </summary>
internal static class InStreamWriter
{
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
        var buffer = new byte[chunkSize];
        var lengthBuffer = new byte[4];
        long totalBytes = 0;

        int bytesRead;
#if !NETSTANDARD2_0
        while ((bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
#else
        while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
#endif
        {
            totalBytes += bytesRead;
            if (totalBytes > maxStreamSize)
            {
                throw new ClamStreamSizeExceededException(maxStreamSize);
            }

            BinaryPrimitives.WriteUInt32BigEndian(lengthBuffer, (uint)bytesRead);
#if !NETSTANDARD2_0
            await destination.WriteAsync(lengthBuffer, cancellationToken).ConfigureAwait(false);
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
#else
            await destination.WriteAsync(lengthBuffer, 0, lengthBuffer.Length, cancellationToken).ConfigureAwait(false);
            await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
#endif
        }

        // Zero-length chunk signals end of stream to clamd
        Array.Clear(lengthBuffer, 0, 4);
#if !NETSTANDARD2_0
        await destination.WriteAsync(lengthBuffer, cancellationToken).ConfigureAwait(false);
#else
        await destination.WriteAsync(lengthBuffer, 0, lengthBuffer.Length, cancellationToken).ConfigureAwait(false);
#endif
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}