using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ClamClient.Net.Tests.Fakes;

/// <summary>
/// An in-process TCP server that accepts one connection, handles an IDSESSION command loop,
/// records bytes of the first real command, and sends back scripted responses.
/// Enables testing ClamClient without a real clamd.
/// </summary>
internal sealed class FakeClamdServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly TcpListener _listener;
    private readonly string[] _responses;
    private readonly Task _serverTask;
    private int _connectionCount;

    private FakeClamdServer(string[] responses)
    {
        _responses = responses;
        _listener = new(IPAddress.Loopback, 0);
        _listener.Start();
        _serverTask = HandleOneConnectionAsync();
    }

    /// <summary>
    /// Number of TCP connections accepted so far.
    /// </summary>
    public int ConnectionCount => _connectionCount;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public byte[] ReceivedBytes { get; private set; } = [];

    public static FakeClamdServer Start(params string[] responses) => new(responses);

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        try
        {
            await _serverTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        _cts.Dispose();
    }

    // Mirrors what real clamd does in IDSESSION mode: prefix each response line with "N: ".
    // Multi-line responses (MULTISCAN) have each line prefixed; the null terminator is preserved.
    private static string PrefixWithSequenceNumber(string response, int seq)
    {
        var trimmed = response.TrimEnd('\0');
        var lines = trimmed.Split('\n');
        var prefixed = string.Join("\n", lines.Select(l => l.Length > 0 ? $"{seq}: {l}" : l));
        return prefixed + "\0";
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Connection closed before all bytes were received.");
            }

            offset += read;
        }
    }

    private static async Task ReadInstreamPayloadAsync(Stream stream, MemoryStream ms, CancellationToken ct)
    {
        var lengthBuf = new byte[4];
        while (true)
        {
            await ReadExactAsync(stream, lengthBuf, ct).ConfigureAwait(false);
            ms.Write(lengthBuf, 0, 4);
            var chunkLength = (lengthBuf[0] << 24) | (lengthBuf[1] << 16) | (lengthBuf[2] << 8) | lengthBuf[3];
            if (chunkLength == 0)
            {
                return;
            }

            var chunk = new byte[chunkLength];
            await ReadExactAsync(stream, chunk, ct).ConfigureAwait(false);
            ms.Write(chunk, 0, chunk.Length);
        }
    }

    // Returns true if a null-terminated command was read; false if the connection closed (EOF before any byte).
    private static async Task<bool> ReadUntilNullAsync(Stream stream, MemoryStream ms, CancellationToken ct)
    {
        var buf = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buf, 0, 1, ct).ConfigureAwait(false);
            if (read == 0)
            {
                return ms.Length > 0; // false if nothing was read yet
            }

            ms.Write(buf, 0, 1);
            if (buf[0] == 0)
            {
                return true;
            }
        }
    }

    private async Task HandleOneConnectionAsync()
    {
        TcpClient client;
        try
        {
            client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return;
        }

        Interlocked.Increment(ref _connectionCount);

        using (client)
        {
            var stream = client.GetStream();
            var responseIndex = 0;
            var firstRealCommand = true;
            var sequenceNumber = 1;

            while (true)
            {
                using var ms = new MemoryStream();

                try
                {
                    var read = await ReadUntilNullAsync(stream, ms, _cts.Token).ConfigureAwait(false);
                    if (!read)
                    {
                        // Client closed the connection cleanly.
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var cmdText = Encoding.ASCII.GetString(ms.ToArray()).TrimEnd('\0');

                // IDSESSION — acknowledge silently, no response sent.
                if (cmdText.EndsWith("IDSESSION", StringComparison.Ordinal))
                {
                    continue;
                }

                // END — client is closing the session.
                if (cmdText.EndsWith("END", StringComparison.Ordinal))
                {
                    break;
                }

                // Real command — capture the first one's wire bytes into ReceivedBytes.
                if (firstRealCommand)
                {
                    firstRealCommand = false;

                    if (cmdText.EndsWith("INSTREAM", StringComparison.Ordinal))
                    {
                        try
                        {
                            await ReadInstreamPayloadAsync(stream, ms, _cts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }

                    ReceivedBytes = ms.ToArray();
                }

                if (responseIndex < _responses.Length)
                {
                    // Real clamd in IDSESSION mode prefixes each response line with the sequence number.
                    var rawResponse = _responses[responseIndex++];
                    var seq = sequenceNumber++;
                    var prefixed = PrefixWithSequenceNumber(rawResponse, seq);
                    var responseBytes = Encoding.ASCII.GetBytes(prefixed);
                    try
                    {
                        await stream.WriteAsync(responseBytes, 0, responseBytes.Length, _cts.Token).ConfigureAwait(false);
                        await stream.FlushAsync(_cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
    }
}