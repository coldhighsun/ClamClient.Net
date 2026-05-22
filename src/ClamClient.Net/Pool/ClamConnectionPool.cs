using ClamClient.Net.Configuration;
using ClamClient.Net.Exceptions;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace ClamClient.Net.Pool;

/// <summary>
/// Manages a pool of reusable IDSESSION connections to clamd.
/// </summary>
internal sealed class ClamConnectionPool : IAsyncDisposable
{
    /// <summary>
    /// Pre-encoded wire bytes for the IDSESSION handshake.
    /// </summary>
    private static readonly byte[] IdSessionBytes = "zIDSESSION\0"u8.ToArray();

    /// <summary>
    /// Queue of connections currently idle and available for reuse.
    /// </summary>
    private readonly ConcurrentQueue<ClamConnection> _idle = new();

    /// <summary>
    /// Configuration used to evaluate pool limits and idle timeout.
    /// </summary>
    private readonly ClamClientOptions _options;

    /// <summary>
    /// Caps the number of concurrent connections; <see langword="null"/> when <see cref="ClamClientOptions.MaxConnections"/> is zero (unlimited).
    /// </summary>
    private readonly SemaphoreSlim? _semaphore;

    /// <summary>
    /// Factory that opens a new raw stream to clamd on demand.
    /// </summary>
    private readonly Func<CancellationToken, Task<Stream>> _streamFactory;

    /// <summary>
    /// Set to <see langword="true"/> once <see cref="DisposeAsync"/> is called to reject further rentals.
    /// </summary>
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new pool using the supplied options and stream factory.
    /// </summary>
    /// <param name="options">Configuration options for the pool and its connections.</param>
    /// <param name="streamFactory">Factory that opens a new raw stream to clamd on demand.</param>
    internal ClamConnectionPool(
        ClamClientOptions options,
        Func<CancellationToken, Task<Stream>> streamFactory)
    {
        _options = options;
        _streamFactory = streamFactory;

        if (options.MaxConnections > 0)
        {
            _semaphore = new(options.MaxConnections, options.MaxConnections);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        var connections = new List<ClamConnection>();
        while (_idle.TryDequeue(out var conn))
        {
            connections.Add(conn);
        }

        var disposeTasks = connections.Select(c => c.DisposeAsync().AsTask()).ToArray();
        try
        {
            await Task.WhenAll(disposeTasks).ConfigureAwait(false);
        }
        catch
        {
            // best effort — underlying sockets may already be dead
        }

        _semaphore?.Dispose();
    }

    /// <summary>
    /// Retrieves an idle connection from the pool, or opens a new one if none are available.
    /// </summary>
    internal async Task<ClamConnection> RentAsync(CancellationToken ct)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ClamConnectionPool));
        }

        if (_semaphore is not null)
        {
            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        }

        // Try to reuse an idle connection, discarding stale ones.
        while (_idle.TryDequeue(out var candidate))
        {
            if (candidate.IsValid(_options.IdleConnectionTimeout))
            {
                return candidate;
            }

            // Stale — discard but keep the semaphore slot for the new connection below.
            _ = candidate.DisposeAsync();
        }

        // No valid idle connection; open a new one.
        try
        {
            var stream = await _streamFactory(ct).ConfigureAwait(false);
            await SendIdSessionAsync(stream, ct).ConfigureAwait(false);
            return new(stream);
        }
        catch (SocketException ex)
        {
            _semaphore?.Release();
            throw new ClamConnectionException($"Failed to connect to clamd at {_options.Endpoint}.", ex);
        }
        catch (IOException ex)
        {
            _semaphore?.Release();
            throw new ClamConnectionException($"Failed to connect to clamd at {_options.Endpoint}.", ex);
        }
        catch
        {
            _semaphore?.Release();
            throw;
        }
    }

    /// <summary>
    /// Returns a connection to the pool, or disposes it if it is unhealthy or stale.
    /// </summary>
    internal void Return(ClamConnection connection)
    {
        if (_disposed || !connection.IsHealthy || !connection.IsValid(_options.IdleConnectionTimeout))
        {
            _ = connection.DisposeAsync();
            _semaphore?.Release();
            return;
        }

        _idle.Enqueue(connection);

        // Semaphore is NOT released — the slot remains occupied by the idle connection.
    }

    /// <summary>
    /// Sends the IDSESSION handshake that enables multi-command mode on the connection.
    /// </summary>
    private static async Task SendIdSessionAsync(Stream stream, CancellationToken ct)
    {
        await stream.WriteAsync(IdSessionBytes, 0, IdSessionBytes.Length, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}