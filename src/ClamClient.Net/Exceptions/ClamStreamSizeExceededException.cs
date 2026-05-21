namespace ClamClient.Net.Exceptions;

/// <summary>
/// Thrown when the input stream exceeds the configured maximum stream size.
/// </summary>
public sealed class ClamStreamSizeExceededException : Exception
{
    /// <inheritdoc/>
    public ClamStreamSizeExceededException()
    {
    }

    /// <inheritdoc/>
    public ClamStreamSizeExceededException(string message) : base(message) { }

    /// <inheritdoc/>
    public ClamStreamSizeExceededException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>
    /// Initializes the exception with the configured maximum size.
    /// </summary>
    public ClamStreamSizeExceededException(long maxStreamSize)
        : base($"Stream exceeds the maximum allowed size of {maxStreamSize:N0} bytes.")
    {
        MaxStreamSize = maxStreamSize;
    }

    /// <summary>
    /// The configured maximum stream size in bytes.
    /// </summary>
    public long MaxStreamSize
    {
        get;
    }
}