using ClamClient.Net.Abstractions;
using ClamClient.Net.Configuration;
using ClamClient.Net.Exceptions;
using ClamClient.Net.Protocol;
using ClamClient.Net.Results;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Text;

namespace ClamClient.Net;

/// <summary>
/// Default implementation of <see cref="IClamClient"/>.
/// </summary>
public sealed class ClamAVClient : IClamClient
{
    /// <summary>
    /// Provides configuration options for the ClamAV client.
    /// </summary>
    private readonly ClamClientOptions _options;

    /// <summary>
    /// Initializes a new instance with the supplied options.
    /// </summary>
    public ClamAVClient(ClamClientOptions options)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(options);
#else
        if (options is null) throw new ArgumentNullException(nameof(options));
#endif
        _options = options;
    }

    /// <summary>
    /// Initializes a new instance with default options (TCP localhost:3310).
    /// </summary>
    public ClamAVClient() : this(new()) { }

    /// <inheritdoc/>
    public Task<string> GetStatsAsync(CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync("STATS", afterCommand: null, cancellationToken);

    /// <inheritdoc/>
    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync("VERSION", afterCommand: null, cancellationToken);

    /// <inheritdoc/>
    public async Task<ScanResult> MultiScanAsync(string filePath, CancellationToken cancellationToken = default)
    {
#if NET7_0_OR_GREATER
        ArgumentException.ThrowIfNullOrEmpty(filePath);
#else
        if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("Value cannot be null or empty.", nameof(filePath));
#endif
        var raw = await ExecuteCommandAsync($"MULTISCAN {filePath}", afterCommand: null, cancellationToken).ConfigureAwait(false);
        return ClamResponseParser.ParseScanResponse(raw);
    }

    /// <inheritdoc/>
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        var raw = await ExecuteCommandAsync("PING", afterCommand: null, cancellationToken).ConfigureAwait(false);
        return ClamResponseParser.IsPong(raw);
    }

    /// <inheritdoc/>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteCommandAsync("RELOAD", afterCommand: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ScanResult> ScanFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
#if NET7_0_OR_GREATER
        ArgumentException.ThrowIfNullOrEmpty(filePath);
#else
        if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("Value cannot be null or empty.", nameof(filePath));
#endif
        var raw = await ExecuteCommandAsync($"SCAN {filePath}", afterCommand: null, cancellationToken).ConfigureAwait(false);
        return ClamResponseParser.ParseScanResponse(raw);
    }

    /// <inheritdoc/>
    public async Task<ScanResult> ScanStreamAsync(Stream data, CancellationToken cancellationToken = default)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(data);
#else
        if (data is null) throw new ArgumentNullException(nameof(data));
#endif
        var raw = await ExecuteCommandAsync(
            "INSTREAM",
            (stream, ct) => InStreamWriter.WriteAsync(data, stream, _options.ChunkSize, _options.MaxStreamSize, ct),
            cancellationToken).ConfigureAwait(false);
        return ClamResponseParser.ParseScanResponse(raw);
    }

    /// <inheritdoc/>
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteCommandAsync("SHUTDOWN", afterCommand: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Establishes a connection to the clamd server based on the configured endpoint.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A stream representing the connection to the clamd server.</returns>
    [SuppressMessage("Reliability", "CA2000", Justification = "Ownership transferred to NetworkStream via ownsSocket: true")]
    private async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
    {
        var endpoint = _options.Endpoint;
        var timeoutMs = (int)_options.Timeout.TotalMilliseconds;

        if (endpoint.IsUnixSocket)
        {
#if NET5_0_OR_GREATER
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
#if NET5_0_OR_GREATER
            var client = new TcpClient();
            client.SendTimeout = timeoutMs;
            client.ReceiveTimeout = timeoutMs;
            await client.ConnectAsync(endpoint.Host!, endpoint.Port, cancellationToken).ConfigureAwait(false);
            return client.GetStream();
#else
            var client = new TcpClient();
            client.SendTimeout = timeoutMs;
            client.ReceiveTimeout = timeoutMs;
            // CancellationToken overload not available; connect synchronously on the thread-pool
            await Task.Run(() => client.Connect(endpoint.Host!, endpoint.Port), cancellationToken).ConfigureAwait(false);
            return client.GetStream();
#endif
        }
    }

    /// <summary>
    /// Sends a command to the ClamAV daemon asynchronously and returns the response.
    /// </summary>
    /// <param name="command">The command to send to the ClamAV daemon.</param>
    /// <param name="afterCommand">An optional delegate to execute after sending the command.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, containing the response from the ClamAV daemon.</returns>
    /// <exception cref="ClamConnectionException">Thrown when a connection to the ClamAV daemon cannot be established.</exception>
    private async Task<string> ExecuteCommandAsync(
        string command,
        Func<Stream, CancellationToken, Task>? afterCommand,
        CancellationToken cancellationToken)
    {
        Stream stream;
        try
        {
            stream = await ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            throw new ClamConnectionException($"Failed to connect to clamd at {_options.Endpoint}.", ex);
        }
        catch (IOException ex)
        {
            throw new ClamConnectionException($"Failed to connect to clamd at {_options.Endpoint}.", ex);
        }

#if NET6_0_OR_GREATER
        await using (stream.ConfigureAwait(false))
#else
        using (stream)
#endif
        {
            // z-prefix: clamd uses null-terminated framing throughout
            var commandBytes = Encoding.ASCII.GetBytes($"z{command}\0");
#if NET6_0_OR_GREATER
            await stream.WriteAsync(commandBytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
#else
            await stream.WriteAsync(commandBytes, 0, commandBytes.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
#endif

            if (afterCommand is not null)
                await afterCommand(stream, cancellationToken).ConfigureAwait(false);

            using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
#if NET7_0_OR_GREATER
            var response = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
#else
            var response = await reader.ReadToEndAsync().ConfigureAwait(false);
#endif
            return response.TrimEnd('\0');
        }
    }
}
