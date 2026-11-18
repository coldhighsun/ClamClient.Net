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
    /// The validated connect/read/write timeout, snapshotted at construction so later changes to the mutable options cannot bypass validation.
    /// </summary>
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Manages a pool of connections to the clamd server for efficient reuse across multiple operations.
    /// </summary>
    private readonly ClamConnectionPool _pool;

    /// <summary>
    /// Initializes a new instance with the supplied options.
    /// </summary>
    /// <param name="options">The client options.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <see cref="ClamClientOptions.Timeout"/> is not positive or exceeds <see cref="int.MaxValue"/> milliseconds.</exception>
    public ClamAVClient(ClamClientOptions options)
        : this(options, streamFactory: null)
    {
    }

    /// <summary>
    /// Initializes a new instance with the supplied options and an optional connection factory, so tests can substitute the transport.
    /// </summary>
    /// <param name="options">The client options.</param>
    /// <param name="streamFactory">Opens a new stream to clamd; when <see langword="null"/>, the endpoint from <paramref name="options"/> is used.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <see cref="ClamClientOptions.Timeout"/> is not positive or exceeds <see cref="int.MaxValue"/> milliseconds.</exception>
    internal ClamAVClient(ClamClientOptions options, Func<CancellationToken, Task<Stream>>? streamFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.Timeout <= TimeSpan.Zero || options.Timeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                "options.Timeout", options.Timeout, "Timeout must be positive and at most int.MaxValue milliseconds.");
        }

        _timeout = options.Timeout;

        _pool = new(options, streamFactory ?? ConnectAsync);
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

        Task WriteData(Stream dest, CancellationToken ct) =>
            InStreamWriter.WriteAsync(data, dest, _options.ChunkSize, _options.MaxStreamSize, ct);

        try
        {
            var raw = await ExecuteOnceAsync(ClamConnection.InStreamBytes, WriteData, cancellationToken).ConfigureAwait(false);
            return ClamResponseParser.ParseScanResponse(raw);
        }
        catch (ClamStreamSizeExceededException)
        {
            // Stream too large — connection is already marked unhealthy by ExecuteOnceAsync's catch block.
            return ScanResult.CreateStreamTooLarge(_options.MaxStreamSize);
        }
        catch (ClamConnectionTimeoutException)
        {
            // Connect timeouts are not stale-connection failures; retrying would only double the wait.
            throw;
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
            try
            {
                var raw = await ExecuteOnceAsync(ClamConnection.InStreamBytes, WriteData, cancellationToken).ConfigureAwait(false);
                return ClamResponseParser.ParseScanResponse(raw);
            }
            catch (ClamStreamSizeExceededException)
            {
                return ScanResult.CreateStreamTooLarge(_options.MaxStreamSize);
            }
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
        var timeoutMs = (int)_timeout.TotalMilliseconds;

        if (endpoint.IsUnixSocket)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                socket.SendTimeout = timeoutMs;
                socket.ReceiveTimeout = timeoutMs;
                var unixEndPoint = new UnixDomainSocketEndPoint(endpoint.UnixSocketPath!);
                await ConnectWithTimeoutAsync(ct => socket.ConnectAsync(unixEndPoint, ct), _timeout, cancellationToken)
                    .ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                // Connect failed or was cancelled: the stream never took ownership, so release the socket here.
                socket.Dispose();
                throw;
            }
        }

        return await ConnectTcpAsync(new TcpClient(), endpoint.Host!, endpoint.Port, _timeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Connects the supplied <see cref="TcpClient"/> and returns its stream. The client is disposed if
    /// the connection attempt fails or is cancelled; on success the returned stream owns the socket.
    /// </summary>
    /// <param name="client">The client to connect. Ownership is taken by this method.</param>
    /// <param name="host">The remote host name or address.</param>
    /// <param name="port">The remote TCP port.</param>
    /// <param name="timeout">The connect timeout, also applied as the send/receive timeout of the client.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A stream representing the open connection.</returns>
    /// <exception cref="SocketException">Thrown with <see cref="SocketError.TimedOut"/> when the connect exceeds <paramref name="timeout"/>.</exception>
    internal static async Task<Stream> ConnectTcpAsync(
        TcpClient client,
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var timeoutMs = (int)timeout.TotalMilliseconds;
            client.SendTimeout = timeoutMs;
            client.ReceiveTimeout = timeoutMs;

            await ConnectWithTimeoutAsync(ct => client.ConnectAsync(host, port, ct), timeout, cancellationToken)
                .ConfigureAwait(false);
            return client.GetStream();
        }
        catch
        {
            // Connect failed, timed out or was cancelled: release the client instead of waiting for the finalizer.
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs a connect operation under a timeout. Neither <see cref="Socket.SendTimeout"/> nor
    /// <see cref="Socket.ReceiveTimeout"/> applies to connecting, so the timeout is enforced via cancellation.
    /// </summary>
    /// <param name="connect">The connect operation; it must honor the token it is given.</param>
    /// <param name="timeout">The maximum time the connect may take.</param>
    /// <param name="cancellationToken">The caller's token; cancelling it propagates <see cref="OperationCanceledException"/>.</param>
    /// <returns>A task that completes when the connect has finished.</returns>
    /// <exception cref="SocketException">Thrown with <see cref="SocketError.TimedOut"/> when <paramref name="timeout"/> elapses first.</exception>
    internal static async Task ConnectWithTimeoutAsync(
        Func<CancellationToken, ValueTask> connect,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await connect(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Only the timeout fired, not the caller: report it as a connect failure so the pool wraps it
            // in ClamConnectionException instead of letting it look like a caller-initiated cancellation.
            throw new SocketException((int)SocketError.TimedOut);
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
        catch (ClamConnectionException ex) when (afterCommand is null && ex is not ClamConnectionTimeoutException)
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
        catch (ClamConnectionException ex) when (ex is not ClamConnectionTimeoutException)
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