using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ClamClient.Net.Tests.Fakes;

/// <summary>
/// An in-process TCP server that accepts one connection, records bytes received,
/// and sends back a scripted response. Enables testing ClamClient without a real clamd.
/// </summary>
internal sealed class FakeClamdServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Task _serverTask;
    private readonly string _response;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public byte[] ReceivedBytes { get; private set; } = [];

    private FakeClamdServer(string response)
    {
        _response = response;
        _listener = new(IPAddress.Loopback, 0);
        _listener.Start();
        _serverTask = HandleOneConnectionAsync();
    }

    public static FakeClamdServer Start(string response) => new(response);

    private async Task HandleOneConnectionAsync()
    {
        TcpClient client;
        try
        {
            client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
        }
        catch (SocketException)
        {
            // Listener was stopped before a connection arrived
            return;
        }

        using (client)
        {
            var stream = client.GetStream();
            using var ms = new MemoryStream();

            // Read the null-terminated command (e.g. "zPING\0")
            await ReadUntilNullAsync(stream, ms).ConfigureAwait(false);

            var cmdText = Encoding.ASCII.GetString(ms.ToArray()).TrimEnd('\0');

            // For INSTREAM, also consume the chunk-framed payload so ReceivedBytes is complete
            if (cmdText.EndsWith("INSTREAM", StringComparison.Ordinal))
                await ReadInstreamPayloadAsync(stream, ms).ConfigureAwait(false);

            ReceivedBytes = ms.ToArray();

            var responseBytes = Encoding.ASCII.GetBytes(_response);
            await stream.WriteAsync(responseBytes).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
    }

    // Reads one byte at a time until the null terminator, inclusive.
    private static async Task ReadUntilNullAsync(Stream stream, MemoryStream ms)
    {
        var buf = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buf).ConfigureAwait(false);
            if (read == 0) return;
            ms.Write(buf, 0, 1);
            if (buf[0] == 0) return;
        }
    }

    // Reads INSTREAM chunk frames (4-byte big-endian length + data) until the zero terminator.
    private static async Task ReadInstreamPayloadAsync(Stream stream, MemoryStream ms)
    {
        var lengthBuf = new byte[4];
        while (true)
        {
            await ReadExactAsync(stream, lengthBuf).ConfigureAwait(false);
            ms.Write(lengthBuf);
            var chunkLength = (lengthBuf[0] << 24) | (lengthBuf[1] << 16) | (lengthBuf[2] << 8) | lengthBuf[3];
            if (chunkLength == 0) return;
            var chunk = new byte[chunkLength];
            await ReadExactAsync(stream, chunk).ConfigureAwait(false);
            ms.Write(chunk);
        }
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset)).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Connection closed before all bytes were received.");
            offset += read;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        await _serverTask.ConfigureAwait(false);
    }
}
