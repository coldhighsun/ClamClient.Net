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
    /// <exception cref="Exceptions.ClamConnectionException">Thrown when the connection to clamd is lost after one automatic retry.</exception>
    Task<string> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends VERSION and returns the version string.
    /// </summary>
    /// <exception cref="Exceptions.ClamConnectionException">Thrown when the connection to clamd is lost after one automatic retry.</exception>
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends MULTISCAN for the given path. clamd scans using multiple threads.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filePath"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="Exceptions.ClamConnectionException">Thrown when the connection to clamd is lost after one automatic retry.</exception>
    /// <exception cref="Exceptions.ClamProtocolException">Thrown when clamd returns an empty or unparseable response.</exception>
    Task<ScanResult> MultiScanAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends PING and returns <c>true</c> if clamd replies PONG.
    /// </summary>
    /// <exception cref="Exceptions.ClamConnectionException">Thrown when the connection to clamd is lost after one automatic retry.</exception>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends RELOAD to instruct clamd to reload its virus database.
    /// </summary>
    /// <exception cref="Exceptions.ClamConnectionException">Thrown when the connection to clamd is lost after one automatic retry.</exception>
    Task ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends SCAN for the given file path.
    /// The path must be accessible from clamd's perspective (i.e. on the same host).
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filePath"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="Exceptions.ClamConnectionException">Thrown when the connection to clamd is lost after one automatic retry.</exception>
    /// <exception cref="Exceptions.ClamProtocolException">Thrown when clamd returns an empty or unparseable response.</exception>
    Task<ScanResult> ScanFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams arbitrary data to clamd via INSTREAM for scanning.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="Exceptions.ClamStreamSizeExceededException">Thrown when the stream exceeds the configured <c>MaxStreamSize</c> limit before the data is fully sent.</exception>
    /// <exception cref="Exceptions.ClamConnectionException">Thrown when the connection to clamd is lost and cannot be recovered. Non-seekable streams are not retried on a stale connection.</exception>
    /// <exception cref="Exceptions.ClamProtocolException">Thrown when clamd returns an empty or unparseable response.</exception>
    Task<ScanResult> ScanStreamAsync(Stream data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends SHUTDOWN to terminate the clamd process.
    /// </summary>
    /// <exception cref="Exceptions.ClamConnectionException">Thrown when the connection to clamd is lost after one automatic retry.</exception>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}