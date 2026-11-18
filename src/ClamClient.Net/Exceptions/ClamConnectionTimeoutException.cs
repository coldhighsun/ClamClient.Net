namespace ClamClient.Net.Exceptions;

/// <summary>
/// Thrown when establishing a connection to clamd does not complete within the configured timeout.
/// It is not retried automatically, because retrying would only double the time spent waiting.
/// </summary>
public sealed class ClamConnectionTimeoutException : ClamConnectionException
{
    /// <inheritdoc/>
    public ClamConnectionTimeoutException()
    {
    }

    /// <inheritdoc/>
    public ClamConnectionTimeoutException(string message) : base(message) { }

    /// <inheritdoc/>
    public ClamConnectionTimeoutException(string message, Exception inner) : base(message, inner) { }
}
