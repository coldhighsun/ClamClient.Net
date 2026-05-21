using ClamClient.Net.Results;

namespace ClamClient.Net.Abstractions;

/// <summary>
/// Client for communicating with a clamd daemon.
/// </summary>
public interface IClamClient
{
    /// <summary>
    /// Sends STATS and returns the raw stats block.
    /// </summary>
    Task<string> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends VERSION and returns the version string.
    /// </summary>
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends MULTISCAN for the given path. clamd scans using multiple threads.
    /// </summary>
    Task<ScanResult> MultiScanAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends PING and returns <c>true</c> if clamd replies PONG.
    /// </summary>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends RELOAD to instruct clamd to reload its virus database.
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends SCAN for the given file path.
    /// The path must be accessible from clamd's perspective (i.e. on the same host).
    /// </summary>
    Task<ScanResult> ScanFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams arbitrary data to clamd via INSTREAM for scanning.
    /// </summary>
    Task<ScanResult> ScanStreamAsync(Stream data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends SHUTDOWN to terminate the clamd process.
    /// </summary>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}