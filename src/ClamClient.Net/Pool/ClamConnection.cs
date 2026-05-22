using System.Text;

namespace ClamClient.Net.Pool;

/// <summary>
/// Wraps a stream already placed in IDSESSION mode and provides command execution
/// with null-terminated response framing.
/// </summary>
internal sealed class ClamConnection : IAsyncDisposable
{
    /// <summary>
    /// Pre-encoded wire bytes for the <c>zEND\0</c> command.
    /// </summary>
    internal static readonly byte[] EndBytes = "zEND\0"u8.ToArray();

    /// <summary>
    /// Pre-encoded wire bytes for the <c>zINSTREAM\0</c> command.
    /// </summary>
    internal static readonly byte[] InStreamBytes = "zINSTREAM\0"u8.ToArray();

    /// <summary>
    /// Pre-encoded wire bytes for the <c>zPING\0</c> command.
    /// </summary>
    internal static readonly byte[] PingBytes = "zPING\0"u8.ToArray();

    /// <summary>
    /// Pre-encoded wire bytes for the <c>zRELOAD\0</c> command.
    /// </summary>
    internal static readonly byte[] ReloadBytes = "zRELOAD\0"u8.ToArray();

    /// <summary>
    /// Pre-encoded wire bytes for the <c>zSHUTDOWN\0</c> command.
    /// </summary>
    internal static readonly byte[] ShutdownBytes = "zSHUTDOWN\0"u8.ToArray();

    /// <summary>
    /// Pre-encoded wire bytes for the <c>zSTATS\0</c> command.
    /// </summary>
    internal static readonly byte[] StatsBytes = "zSTATS\0"u8.ToArray();

    /// <summary>
    /// Pre-encoded wire bytes for the <c>zVERSION\0</c> command.
    /// </summary>
    internal static readonly byte[] VersionBytes = "zVERSION\0"u8.ToArray();

    /// <summary>
    /// Shared read buffer for <see cref="ReadNullTerminatedAsync"/>. Safe because a connection
    /// is used by only one caller at a time.
    /// </summary>
    private readonly byte[] _readBuffer = new byte[4096];

    /// <summary>
    /// The underlying TCP or Unix-socket stream for this connection.
    /// </summary>
    private readonly Stream _stream;

    /// <summary>
    /// Tracks whether the connection has encountered an error and should no longer be reused.
    /// </summary>
    private bool _isHealthy = true;

    /// <summary>
    /// Initializes a new instance wrapping the given stream.
    /// </summary>
    internal ClamConnection(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// Gets whether the connection is in a usable state.
    /// </summary>
    internal bool IsHealthy => _isHealthy;

    /// <summary>
    /// Gets the UTC timestamp of the last completed command.
    /// </summary>
    private DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_isHealthy)
        {
            try
            {
                await _stream.WriteAsync(EndBytes, 0, EndBytes.Length).ConfigureAwait(false);
                await _stream.FlushAsync().ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }
        }

#if !NETSTANDARD2_0
        await _stream.DisposeAsync().ConfigureAwait(false);
#else
        _stream.Dispose();
#endif
    }

    /// <summary>
    /// Sends a pre-encoded fixed command and returns the null-terminated response.
    /// Used for commands with no dynamic arguments (PING, RELOAD, STATS, VERSION, INSTREAM, SHUTDOWN).
    /// </summary>
    internal async Task<string> ExecuteAsync(
        byte[] commandBytes,
        Func<Stream, CancellationToken, Task>? afterCommand,
        CancellationToken ct)
    {
        try
        {
            await _stream.WriteAsync(commandBytes, 0, commandBytes.Length, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);

            if (afterCommand is not null)
            {
                await afterCommand(_stream, ct).ConfigureAwait(false);
            }

            var response = await ReadNullTerminatedAsync(ct).ConfigureAwait(false);
            LastUsedAt = DateTime.UtcNow;
            return StripIdSessionPrefixes(response);
        }
        catch
        {
            _isHealthy = false;
            throw;
        }
    }

    /// <summary>
    /// Sends a z-prefixed, null-terminated command with a dynamic string argument and returns the null-terminated response.
    /// Used for commands like SCAN and MULTISCAN that carry a file path.
    /// </summary>
    internal async Task<string> ExecuteAsync(
        string command,
        Func<Stream, CancellationToken, Task>? afterCommand,
        CancellationToken ct)
    {
        try
        {
            // z + command + \0 — dynamic path commands, called infrequently relative to INSTREAM.
            var commandBytes = Encoding.ASCII.GetBytes($"z{command}\0");
            await _stream.WriteAsync(commandBytes, 0, commandBytes.Length, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);

            if (afterCommand is not null)
            {
                await afterCommand(_stream, ct).ConfigureAwait(false);
            }

            var response = await ReadNullTerminatedAsync(ct).ConfigureAwait(false);
            LastUsedAt = DateTime.UtcNow;
            return StripIdSessionPrefixes(response);
        }
        catch
        {
            _isHealthy = false;
            throw;
        }
    }

    /// <summary>
    /// Marks the connection as unhealthy so it will not be returned to the pool.
    /// </summary>
    internal void Invalidate() => _isHealthy = false;

    /// <summary>
    /// Returns <see langword="true"/> if the connection is healthy and has not exceeded the idle timeout.
    /// </summary>
    internal bool IsValid(TimeSpan idleTimeout) =>
        _isHealthy && (DateTime.UtcNow - LastUsedAt) < idleTimeout;

    /// <summary>
    /// Strips leading "ID: " prefixes from each line of the response, which are added by clamd in IDSESSION mode.
    /// </summary>
    /// <param name="response">The raw response from the clamd server.</param>
    /// <returns>The response with IDSESSION prefixes removed.</returns>
    private static string StripIdSessionPrefixes(string response)
    {
        var newlineIdx = response.IndexOf('\n');
        if (newlineIdx < 0)
        {
            return StripOneLine(response);
        }

        // Multi-line path (MULTISCAN). Build the result without Split+Join allocations.
        var sb = new StringBuilder(response.Length);
        var start = 0;
        while (true)
        {
            string line;
            if (newlineIdx < 0)
            {
                line = StripOneLine(response.Substring(start));
                sb.Append(line);
                break;
            }

            var end = newlineIdx;
            line = StripOneLine(response.Substring(start, end - start));
            sb.Append(line);
            sb.Append('\n');
            start = end + 1;
            newlineIdx = response.IndexOf('\n', start);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Removes a numeric prefix followed by a colon and space from the beginning of the specified string.
    /// </summary>
    /// <param name="line">The input string to process.</param>
    /// <returns>A string with the numeric prefix removed if present; otherwise, the original string.</returns>
    private static string StripOneLine(string line)
    {
        var colonIdx = line.IndexOf(": ", StringComparison.Ordinal);
        if (colonIdx <= 0)
        {
            return line;
        }

        for (var i = 0; i < colonIdx; i++)
        {
            if (!char.IsDigit(line[i]))
            {
                return line;
            }
        }

        return line.Substring(colonIdx + 2);
    }

    /// <summary>
    /// Reads bytes into a shared buffer until a null terminator or end-of-stream,
    /// then returns the decoded string. Overflow beyond the buffer falls back to a
    /// <see cref="MemoryStream"/> so arbitrarily long responses (e.g. STATS) are handled correctly.
    /// </summary>
    private async Task<string> ReadNullTerminatedAsync(CancellationToken ct)
    {
        var pos = 0;
        MemoryStream? overflow = null;

        while (true)
        {
            // Fill as much of the remaining buffer as possible in one syscall.
            var available = _readBuffer.Length - pos;
            if (available == 0)
            {
                // Buffer full — spill everything accumulated so far into the overflow stream.
                overflow ??= new MemoryStream(_readBuffer.Length * 2);
                overflow.Write(_readBuffer, 0, pos);
                pos = 0;
                available = _readBuffer.Length;
            }

            var read = await _stream.ReadAsync(_readBuffer, pos, available, ct).ConfigureAwait(false);
            if (read == 0)
            {
                break; // connection closed
            }

            // Scan the newly read bytes for the null terminator.
            var nullIdx = Array.IndexOf(_readBuffer, (byte)0, pos, read);
            if (nullIdx >= 0)
            {
                // Found the terminator; include bytes up to (not including) it.
                pos = nullIdx;
                break;
            }

            pos += read;
        }

        if (overflow is null)
        {
            return Encoding.ASCII.GetString(_readBuffer, 0, pos);
        }

        // Append whatever is left in _readBuffer into the overflow stream.
        overflow.Write(_readBuffer, 0, pos);
        return Encoding.ASCII.GetString(overflow.GetBuffer(), 0, (int)overflow.Length);
    }
}