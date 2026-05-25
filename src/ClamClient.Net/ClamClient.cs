using ClamClient.Net.Abstractions;
using ClamClient.Net.Configuration;
using ClamClient.Net.Exceptions;
using ClamClient.Net.Pool;
using ClamClient.Net.Protocol;
using ClamClient.Net.Results;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;

namespace ClamClient.Net;

/// <summary>
/// Default implementation of <see cref="IClamClient"/>.
/// </summary>
public sealed class ClamAVClient : IClamClient, IAsyncDisposable
{
    /// <summary>
    /// Provides configuration options for the ClamAV client.
    /// </summary>
    private readonly ClamClientOptions _options;

    /// <summary>
    /// Manages a pool of connections to the clamd server for efficient reuse across multiple operations.
    /// </summary>
    private readonly ClamConnectionPool _pool;

    /// <summary>
    /// Initializes a new instance with the supplied options.
    /// </summary>
    public ClamAVClient(ClamClientOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _pool = new ClamConnectionPool(options, ConnectAsync);
    }

    /// <summary>
    /// Initializes a new instance with default options (TCP localhost:3310).
    /// </summary>
    public ClamAVClient() : this(new()) { }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _pool.DisposeAsync();

    /// <inheritdoc/>
    public Task<string> GetStatsAsync(CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(ClamConnection.StatsBytes, afterCommand: null, cancellationToken);

    /// <inheritdoc/>
    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(ClamConnection.VersionBytes, afterCommand: null, cancellationToken);

    /// <inheritdoc/>
    public async Task<ScanResult> MultiScanAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("Value cannot be null or empty.", nameof(filePath));
        }

        var raw = await ExecuteCommandAsync($"MULTISCAN {filePath}", afterCommand: null, cancellationToken).ConfigureAwait(false);
        return ClamResponseParser.ParseScanResponse(raw);
    }

    /// <inheritdoc/>
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        var raw = await ExecuteCommandAsync(ClamConnection.PingBytes, afterCommand: null, cancellationToken).ConfigureAwait(false);
        return ClamResponseParser.IsPong(raw);
    }

    /// <inheritdoc/>
    public Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync(ClamConnection.ReloadBytes, afterCommand: null, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ScanResult> ScanFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("Value cannot be null or empty.", nameof(filePath));
        }

        var raw = await ExecuteCommandAsync($"SCAN {filePath}", afterCommand: null, cancellationToken).ConfigureAwait(false);
        return ClamResponseParser.ParseScanResponse(raw);
    }

    /// <inheritdoc/>
    public async Task<ScanResult> ScanStreamAsync(Stream data, CancellationToken cancellationToken = default)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        var startPosition = data.CanSeek ? data.Position : -1L;

        try
        {
            var raw = await ExecuteOnceAsync(
                ClamConnection.InStreamBytes,
                (stream, ct) => InStreamWriter.WriteAsync(data, stream, _options.ChunkSize, _options.MaxStreamSize, ct),
                cancellationToken).ConfigureAwait(false);
            return ClamResponseParser.ParseScanResponse(raw);
        }
        catch (ClamConnectionException ex) when (startPosition < 0)
        {
            // Non-seekable stream — cannot retry after a stale connection failure.
            throw new ClamConnectionException(
                "Lost connection to clamd and the stream is not seekable; retry is not possible.", ex);
        }
        catch (ClamConnectionException) when (startPosition >= 0)
        {
            // Stale pooled connection — safe to retry: ExecuteOnceAsync's finally block evicts the
            // failed connection via _pool.Return, so the retry always opens a brand-new TCP connection
            // and a fresh IDSESSION. ClamAV never sees data from the dead connection.
            data.Position = startPosition;
            var raw = await ExecuteOnceAsync(
                ClamConnection.InStreamBytes,
                (stream, ct) => InStreamWriter.WriteAsync(data, stream, _options.ChunkSize, _options.MaxStreamSize, ct),
                cancellationToken).ConfigureAwait(false);
            return ClamResponseParser.ParseScanResponse(raw);
        }
    }

    /// <inheritdoc/>
    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync(ClamConnection.ShutdownBytes, afterCommand: null, cancellationToken);
    }

    /// <summary>
    /// Establishes a raw connection to the clamd server based on the configured endpoint.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A stream representing the open connection.</returns>
    [SuppressMessage("Reliability", "CA2000", Justification = "Ownership transferred to NetworkStream via ownsSocket: true")]
    private async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
    {
        var endpoint = _options.Endpoint;
        var timeoutMs = (int)_options.Timeout.TotalMilliseconds;

        if (endpoint.IsUnixSocket)
        {
#if !NETSTANDARD2_0
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.SendTimeout = timeoutMs;
            socket.ReceiveTimeout = timeoutMs;
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.UnixSocketPath!), cancellationToken)
                .ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
#else
            throw new PlatformNotSupportedException("Unix domain sockets require .NET 5 or later.");
#endif
        }
        else
        {
            var client = new TcpClient();
            client.SendTimeout = timeoutMs;
            client.ReceiveTimeout = timeoutMs;

#if !NETSTANDARD2_0
            await client.ConnectAsync(endpoint.Host!, endpoint.Port, cancellationToken).ConfigureAwait(false);
#else
            // CancellationToken overload not available; connect synchronously on the thread-pool
            await Task.Run(() => client.Connect(endpoint.Host!, endpoint.Port), cancellationToken).ConfigureAwait(false);
#endif
            return client.GetStream();
        }
    }

    /// <summary>
    /// Rents a pooled connection, sends a pre-encoded fixed command, and returns the connection to the pool.
    /// Retries once with a fresh connection when <paramref name="afterCommand"/> is <see langword="null"/>
    /// and the pooled connection turns out to be stale (e.g. dropped by clamd after a RELOAD).
    /// </summary>
    /// <param name="commandBytes">The pre-encoded wire bytes for the command.</param>
    /// <param name="afterCommand">Optional delegate invoked after the command is written, e.g. to stream data.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The null-terminated response string from clamd.</returns>
    private async Task<string> ExecuteCommandAsync(
        byte[] commandBytes,
        Func<Stream, CancellationToken, Task>? afterCommand,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteOnceAsync(commandBytes, afterCommand, cancellationToken).ConfigureAwait(false);
        }
        catch (ClamConnectionException) when (afterCommand is null)
        {
            // Stale pooled connection — the failed connection was already evicted; retry with a fresh one.
            return await ExecuteOnceAsync(commandBytes, afterCommand: null, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Rents a pooled connection, sends a dynamic command string, and returns the connection to the pool.
    /// Retries once with a fresh connection if the pooled connection is stale.
    /// Used for commands that carry a file path argument (SCAN, MULTISCAN).
    /// </summary>
    /// <param name="command">The command name and arguments to send (without the z-prefix or null terminator).</param>
    /// <param name="afterCommand">Optional delegate invoked after the command is written, e.g. to stream data.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The null-terminated response string from clamd.</returns>
    private async Task<string> ExecuteCommandAsync(
        string command,
        Func<Stream, CancellationToken, Task>? afterCommand,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteOnceAsync(command, afterCommand, cancellationToken).ConfigureAwait(false);
        }
        catch (ClamConnectionException)
        {
            // Stale pooled connection — retry once with a fresh one.
            return await ExecuteOnceAsync(command, afterCommand, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Single attempt: rents a connection, executes the pre-encoded command, returns the connection to the pool.
    /// </summary>
    private async Task<string> ExecuteOnceAsync(
        byte[] commandBytes,
        Func<Stream, CancellationToken, Task>? afterCommand,
        CancellationToken ct)
    {
        var connection = await _pool.RentAsync(ct).ConfigureAwait(false);
        try
        {
            return await connection.ExecuteAsync(commandBytes, afterCommand, ct).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            throw new ClamConnectionException($"Lost connection to clamd at {_options.Endpoint}.", ex);
        }
        catch (IOException ex)
        {
            throw new ClamConnectionException($"Lost connection to clamd at {_options.Endpoint}.", ex);
        }
        finally
        {
            _pool.Return(connection);
        }
    }

    /// <summary>
    /// Single attempt: rents a connection, executes the dynamic string command, returns the connection to the pool.
    /// </summary>
    private async Task<string> ExecuteOnceAsync(
        string command,
        Func<Stream, CancellationToken, Task>? afterCommand,
        CancellationToken ct)
    {
        var connection = await _pool.RentAsync(ct).ConfigureAwait(false);
        try
        {
            return await connection.ExecuteAsync(command, afterCommand, ct).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            throw new ClamConnectionException($"Lost connection to clamd at {_options.Endpoint}.", ex);
        }
        catch (IOException ex)
        {
            throw new ClamConnectionException($"Lost connection to clamd at {_options.Endpoint}.", ex);
        }
        finally
        {
            _pool.Return(connection);
        }
    }
}