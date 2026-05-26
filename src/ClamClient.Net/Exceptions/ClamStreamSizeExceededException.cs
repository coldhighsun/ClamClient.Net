namespace ClamClient.Net.Exceptions;

/// <summary>
/// Thrown internally when the input stream exceeds the configured maximum stream size.
/// <see cref="ClamAVClient"/> catches this and returns a <see cref="Results.ScanStatus.StreamTooLarge"/> result
/// instead of propagating it to the caller.
/// </summary>
internal sealed class ClamStreamSizeExceededException : Exception
{
    /// <summary>
    /// Initializes the exception with the configured maximum size.
    /// </summary>
    internal ClamStreamSizeExceededException(long maxStreamSize)
        : base($"Stream exceeds the maximum allowed size of {maxStreamSize:N0} bytes.")
    {
        MaxStreamSize = maxStreamSize;
    }

    /// <summary>
    /// The configured maximum stream size in bytes.
    /// </summary>
    internal long MaxStreamSize
    {
        get;
    }
}