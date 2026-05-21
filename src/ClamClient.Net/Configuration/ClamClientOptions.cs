namespace ClamClient.Net.Configuration;

/// <summary>
/// Configuration options for <see cref="ClamAVClient"/>.
/// </summary>
public sealed class ClamClientOptions
{
    /// <summary>
    /// Maximum bytes per INSTREAM chunk. Defaults to 128 KB.
    /// Must not exceed clamd's StreamMaxLength setting.
    /// </summary>
    public int ChunkSize { get; set; } = 128 * 1024;

    /// <summary>
    /// The clamd endpoint to connect to. Defaults to TCP localhost:3310.
    /// </summary>
    public ClamEndpoint Endpoint { get; set; } = ClamEndpoint.Tcp("localhost");

    /// <summary>
    /// Hard cap on total INSTREAM payload size. Defaults to 25 MB.
    /// </summary>
    public long MaxStreamSize { get; set; } = 25L * 1024 * 1024;

    /// <summary>
    /// Socket connect and read timeout. Defaults to 10 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}