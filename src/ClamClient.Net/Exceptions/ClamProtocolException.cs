namespace ClamClient.Net.Exceptions;

/// <summary>
/// Thrown when clamd returns an unexpected or unparseable response.
/// </summary>
public sealed class ClamProtocolException : Exception
{
    /// <inheritdoc/>
    public ClamProtocolException()
    {
    }

    /// <inheritdoc/>
    public ClamProtocolException(string message) : base(message) { }

    /// <inheritdoc/>
    public ClamProtocolException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>
    /// Initializes the exception with the raw clamd response.
    /// </summary>
    public ClamProtocolException(string rawResponse, bool isRawResponse)
        : base($"Unexpected clamd response: '{rawResponse}'")
    {
        _ = isRawResponse;
        RawResponse = rawResponse;
    }

    /// <summary>
    /// The raw response text received from clamd.
    /// </summary>
    public string RawResponse { get; } = string.Empty;
}