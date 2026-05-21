namespace ClamClient.Net.Exceptions;

/// <summary>
/// Thrown when a connection to clamd cannot be established or is lost.
/// </summary>
public sealed class ClamConnectionException : Exception
{
    /// <inheritdoc/>
    public ClamConnectionException()
    {
    }

    /// <inheritdoc/>
    public ClamConnectionException(string message) : base(message) { }

    /// <inheritdoc/>
    public ClamConnectionException(string message, Exception inner) : base(message, inner) { }
}