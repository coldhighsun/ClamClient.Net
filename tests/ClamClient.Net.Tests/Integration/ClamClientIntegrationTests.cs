using ClamClient.Net.Configuration;
using ClamClient.Net.Exceptions;
using ClamClient.Net.Results;
using ClamClient.Net.Tests.Fakes;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ClamClient.Net.Tests.Integration;

/// <summary>
/// Integration-style tests using FakeClamdServer — no real clamd required.
/// </summary>
public sealed class ClamClientIntegrationTests
{
    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetStatsAsync_ReturnsStatsString()
    {
        const string stats = "POOLS: 1\nSTATE: VALID PRIMARY\nTHREADS: live 1\nQUEUE: 0\nMEMSTATS: heap N/A\nEND";
        await using var server = FakeClamdServer.Start(stats + "\0");
        await using var client = BuildClient(server.Port);

        var result = await client.GetStatsAsync(TestCt);

        Assert.Contains("POOLS", result);
    }

    [Fact]
    public async Task GetStatsAsync_SendsCorrectCommand()
    {
        const string stats = "POOLS: 1\nEND";
        await using var server = FakeClamdServer.Start(stats + "\0");
        await using var client = BuildClient(server.Port);

        await client.GetStatsAsync(TestCt);

        var sentText = Encoding.ASCII.GetString(server.ReceivedBytes);
        Assert.Equal("zSTATS\0", sentText);
    }

    [Fact]
    public async Task GetVersionAsync_ReturnsVersionString()
    {
        const string version = "ClamAV 1.0.0/26800/Mon Oct 30 08:59:00 2023";
        await using var server = FakeClamdServer.Start(version + "\0");
        await using var client = BuildClient(server.Port);

        var result = await client.GetVersionAsync(TestCt);

        Assert.Equal(version, result);
    }

    [Fact]
    public async Task MultipleCommandsOnSameClient_ReusesSingleConnection()
    {
        // server declared first so it disposes last; client disposes first and sends zEND\0.
        await using var server = FakeClamdServer.Start("PONG\0", "PONG\0", "PONG\0");
        await using var client = BuildClient(server.Port);

        await client.PingAsync(TestCt);
        await client.PingAsync(TestCt);
        await client.PingAsync(TestCt);

        Assert.Equal(1, server.ConnectionCount);
    }

    [Fact]
    public async Task MultiScanAsync_CleanDirectory_ReturnsCleanResult()
    {
        await using var server = FakeClamdServer.Start("/tmp/dir/file.txt: OK\0");
        await using var client = BuildClient(server.Port);

        var result = await client.MultiScanAsync("/tmp/dir", TestCt);

        Assert.Equal(ScanStatus.Clean, result.Status);
        Assert.Empty(result.Threats);
    }

    [Fact]
    public async Task MultiScanAsync_EmptyFilePath_ThrowsArgumentException()
    {
        await using var client = BuildClient(port: 0);

        await Assert.ThrowsAsync<ArgumentException>(() => client.MultiScanAsync("", TestCt));
    }

    [Fact]
    public async Task MultiScanAsync_InfectedFile_ReturnsThreatFoundResult()
    {
        await using var server = FakeClamdServer.Start("/tmp/dir/virus.exe: Eicar-Test-Signature FOUND\0");
        await using var client = BuildClient(server.Port);

        var result = await client.MultiScanAsync("/tmp/dir", TestCt);

        Assert.Equal(ScanStatus.ThreatFound, result.Status);
        Assert.Single(result.Threats);
        Assert.Equal("Eicar-Test-Signature", result.Threats[0].ThreatName);
    }

    [Fact]
    public async Task MultiScanAsync_SendsCorrectCommand()
    {
        await using var server = FakeClamdServer.Start("/tmp/dir/file.txt: OK\0");
        await using var client = BuildClient(server.Port);

        await client.MultiScanAsync("/tmp/dir", TestCt);

        var sentText = Encoding.ASCII.GetString(server.ReceivedBytes);
        Assert.Equal("zMULTISCAN /tmp/dir\0", sentText);
    }

    [Fact]
    public async Task PingAsync_ReturnsTrueWhenClamdRepliesPong()
    {
        await using var server = FakeClamdServer.Start("PONG\0");
        await using var client = BuildClient(server.Port);

        var result = await client.PingAsync(TestCt);

        Assert.True(result);
    }

    [Fact]
    public async Task PingAsync_SendsCorrectCommand()
    {
        await using var server = FakeClamdServer.Start("PONG\0");
        await using var client = BuildClient(server.Port);

        await client.PingAsync(TestCt);

        var sentText = Encoding.ASCII.GetString(server.ReceivedBytes);
        Assert.Equal("zPING\0", sentText);
    }

    [Fact]
    public async Task ReloadAsync_SendsCorrectCommand()
    {
        await using var server = FakeClamdServer.Start("RELOADING\0");
        await using var client = BuildClient(server.Port);

        await client.ReloadAsync(TestCt);

        var sentText = Encoding.ASCII.GetString(server.ReceivedBytes);
        Assert.Equal("zRELOAD\0", sentText);
    }

    [Fact]
    public async Task ScanFileAsync_CleanFile_ReturnsCleanResult()
    {
        await using var server = FakeClamdServer.Start("/tmp/test.txt: OK\0");
        await using var client = BuildClient(server.Port);

        var result = await client.ScanFileAsync("/tmp/test.txt", TestCt);

        Assert.Equal(ScanStatus.Clean, result.Status);
    }

    [Fact]
    public async Task ScanFileAsync_EmptyFilePath_ThrowsArgumentException()
    {
        await using var client = BuildClient(port: 0);

        await Assert.ThrowsAsync<ArgumentException>(() => client.ScanFileAsync("", TestCt));
    }

    [Fact]
    public async Task ScanFileAsync_InfectedFile_ReturnsThreatFoundResult()
    {
        await using var server = FakeClamdServer.Start("/tmp/virus.exe: Eicar-Test-Signature FOUND\0");
        await using var client = BuildClient(server.Port);

        var result = await client.ScanFileAsync("/tmp/virus.exe", TestCt);

        Assert.Equal(ScanStatus.ThreatFound, result.Status);
        Assert.Single(result.Threats);
        Assert.Equal("Eicar-Test-Signature", result.Threats[0].ThreatName);
    }

    [Fact]
    public async Task ScanStreamAsync_StreamExceedsMaxSize_ReturnsStreamTooLargeResult()
    {
        await using var server = FakeClamdServer.Start("stream: OK\0");
        await using var client = new ClamAVClient(new()
        {
            Endpoint = ClamEndpoint.Tcp("127.0.0.1", server.Port),
            Timeout = TimeSpan.FromSeconds(5),
            MaxStreamSize = 10
        });
        var data = new MemoryStream(new byte[100]);

        var result = await client.ScanStreamAsync(data, TestCt);

        Assert.Equal(ScanStatus.StreamTooLarge, result.Status);
        Assert.Empty(result.Threats);
    }

    [Fact]
    public async Task ScanStreamAsync_CleanData_ReturnsCleanResult()
    {
        await using var server = FakeClamdServer.Start("stream: OK\0");
        await using var client = BuildClient(server.Port);
        var data = new MemoryStream("Hello, world!"u8.ToArray());

        var result = await client.ScanStreamAsync(data, TestCt);

        Assert.Equal(ScanStatus.Clean, result.Status);
    }

    [Fact]
    public async Task ScanStreamAsync_InfectedData_ReturnsThreatFoundResult()
    {
        await using var server = FakeClamdServer.Start("stream: Eicar-Test-Signature FOUND\0");
        await using var client = BuildClient(server.Port);
        var data = new MemoryStream("X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"u8.ToArray());

        var result = await client.ScanStreamAsync(data, TestCt);

        Assert.Equal(ScanStatus.ThreatFound, result.Status);
        Assert.Equal("stream", result.Threats[0].FileName);
        Assert.Equal("Eicar-Test-Signature", result.Threats[0].ThreatName);
    }

    [Fact]
    public async Task ScanStreamAsync_SendsInstreamCommandWithCorrectFraming()
    {
        await using var server = FakeClamdServer.Start("stream: OK\0");
        await using var client = BuildClient(server.Port);
        var data = new MemoryStream("test"u8.ToArray());

        await client.ScanStreamAsync(data, TestCt);

        // Command starts with "zINSTREAM\0"
        var sentText = Encoding.ASCII.GetString([.. server.ReceivedBytes.Take(10)]);
        Assert.Equal("zINSTREAM\0", sentText);
    }

    [Fact]
    public async Task ShutdownAsync_SendsCorrectCommand()
    {
        await using var server = FakeClamdServer.Start("\0");
        await using var client = BuildClient(server.Port);

        await client.ShutdownAsync(TestCt);

        var sentText = Encoding.ASCII.GetString(server.ReceivedBytes);
        Assert.Equal("zSHUTDOWN\0", sentText);
    }

    /// <summary>
    /// Verifies that a refused connection surfaces as <see cref="ClamConnectionException"/>.
    /// </summary>
    [Fact]
    public async Task PingAsync_NothingListening_ThrowsClamConnectionException()
    {
        var port = GetUnusedPort();
        await using var client = BuildClient(port);

        await Assert.ThrowsAsync<ClamConnectionException>(() => client.PingAsync(TestCt));
    }

    /// <summary>
    /// Verifies that an already-cancelled token surfaces as <see cref="OperationCanceledException"/>.
    /// </summary>
    [Fact]
    public async Task PingAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var port = GetUnusedPort();
        await using var client = BuildClient(port);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PingAsync(cts.Token));
    }

    /// <summary>
    /// Verifies that the <see cref="TcpClient"/> is disposed when the connection attempt fails.
    /// </summary>
    [Fact]
    public async Task ConnectTcpAsync_ConnectionRefused_DisposesClient()
    {
        var port = GetUnusedPort();
        var client = new DisposeTrackingTcpClient();

        await Assert.ThrowsAnyAsync<SocketException>(() =>
            ClamAVClient.ConnectTcpAsync(client, "127.0.0.1", port, TimeSpan.FromSeconds(5), TestCt));

        Assert.True(client.IsDisposed);
    }

    /// <summary>
    /// Verifies that the <see cref="TcpClient"/> is disposed when the connection attempt is cancelled.
    /// </summary>
    [Fact]
    public async Task ConnectTcpAsync_Cancelled_DisposesClient()
    {
        var port = GetUnusedPort();
        var client = new DisposeTrackingTcpClient();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ClamAVClient.ConnectTcpAsync(client, "127.0.0.1", port, TimeSpan.FromSeconds(5), cts.Token));

        Assert.True(client.IsDisposed);
    }

    /// <summary>
    /// Verifies that the <see cref="TcpClient"/> is not disposed on success, because the returned stream owns the socket.
    /// </summary>
    [Fact]
    public async Task ConnectTcpAsync_Success_DoesNotDisposeClient()
    {
        await using var server = FakeClamdServer.Start();
        var client = new DisposeTrackingTcpClient();

        await using var stream = await ClamAVClient.ConnectTcpAsync(
            client, "127.0.0.1", server.Port, TimeSpan.FromSeconds(5), TestCt);

        Assert.False(client.IsDisposed);
    }

    /// <summary>
    /// Verifies that a connect which never completes fails with <see cref="SocketError.TimedOut"/> once the timeout elapses.
    /// </summary>
    [Fact]
    public async Task ConnectWithTimeoutAsync_ConnectNeverCompletes_ThrowsTimedOutSocketException()
    {
        var ex = await Assert.ThrowsAsync<SocketException>(() =>
            ClamAVClient.ConnectWithTimeoutAsync(
                ct => new ValueTask(Task.Delay(Timeout.Infinite, ct)),
                TimeSpan.FromMilliseconds(50),
                TestCt));

        Assert.Equal(SocketError.TimedOut, ex.SocketErrorCode);
    }

    /// <summary>
    /// Verifies that cancellation by the caller is not reported as a timeout.
    /// </summary>
    [Fact]
    public async Task ConnectWithTimeoutAsync_CallerCancels_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ClamAVClient.ConnectWithTimeoutAsync(
                ct => new ValueTask(Task.Delay(Timeout.Infinite, ct)),
                TimeSpan.FromSeconds(30),
                cts.Token));
    }

    /// <summary>
    /// Verifies that a connect which completes in time succeeds.
    /// </summary>
    [Fact]
    public async Task ConnectWithTimeoutAsync_ConnectCompletes_Succeeds()
    {
        await ClamAVClient.ConnectWithTimeoutAsync(_ => ValueTask.CompletedTask, TimeSpan.FromSeconds(30), TestCt);
    }

    /// <summary>
    /// Verifies that a connect timeout surfaces as <see cref="ClamConnectionTimeoutException"/> and the connect is attempted only once.
    /// </summary>
    [Fact]
    public async Task PingAsync_ConnectTimesOut_ThrowsTimeoutExceptionWithoutRetry()
    {
        var attempts = 0;
        await using var client = new ClamAVClient(
            new() { Timeout = TimeSpan.FromSeconds(5) },
            _ =>
            {
                Interlocked.Increment(ref attempts);
                throw new SocketException((int)SocketError.TimedOut);
            });

        await Assert.ThrowsAsync<ClamConnectionTimeoutException>(() => client.PingAsync(TestCt));

        Assert.Equal(1, attempts);
    }

    /// <summary>
    /// Verifies that a non-positive timeout is rejected when the client is constructed.
    /// </summary>
    /// <param name="milliseconds">The invalid timeout in milliseconds.</param>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(int.MaxValue + 1L)]
    public void Constructor_InvalidTimeout_ThrowsArgumentOutOfRangeException(long milliseconds)
    {
        var options = new ClamClientOptions { Timeout = TimeSpan.FromMilliseconds(milliseconds) };

        Assert.Throws<ArgumentOutOfRangeException>(() => new ClamAVClient(options));
    }

    /// <summary>
    /// Verifies that the largest allowed timeout (<see cref="int.MaxValue"/> milliseconds) is accepted.
    /// </summary>
    [Fact]
    public async Task Constructor_TimeoutEqualToIntMaxValueMilliseconds_DoesNotThrow()
    {
        var options = new ClamClientOptions { Timeout = TimeSpan.FromMilliseconds(int.MaxValue) };

        await using var client = new ClamAVClient(options);

        Assert.NotNull(client);
    }

    /// <summary>
    /// Verifies that a connect timeout in the string-command path (SCAN) is not retried.
    /// </summary>
    [Fact]
    public async Task ScanFileAsync_ConnectTimesOut_ThrowsTimeoutExceptionWithoutRetry()
    {
        var attempts = 0;
        await using var client = new ClamAVClient(
            new() { Timeout = TimeSpan.FromSeconds(5) },
            _ =>
            {
                Interlocked.Increment(ref attempts);
                throw new SocketException((int)SocketError.TimedOut);
            });

        await Assert.ThrowsAsync<ClamConnectionTimeoutException>(() => client.ScanFileAsync("/tmp/file", TestCt));

        Assert.Equal(1, attempts);
    }

    /// <summary>
    /// Verifies that a connect timeout during a seekable stream scan is not retried.
    /// </summary>
    [Fact]
    public async Task ScanStreamAsync_ConnectTimesOutOnSeekableStream_ThrowsTimeoutExceptionWithoutRetry()
    {
        var attempts = 0;
        await using var client = new ClamAVClient(
            new() { Timeout = TimeSpan.FromSeconds(5) },
            _ =>
            {
                Interlocked.Increment(ref attempts);
                throw new SocketException((int)SocketError.TimedOut);
            });
        var data = new MemoryStream("test"u8.ToArray());

        await Assert.ThrowsAsync<ClamConnectionTimeoutException>(() => client.ScanStreamAsync(data, TestCt));

        Assert.Equal(1, attempts);
    }

    /// <summary>
    /// Verifies that a non-timeout connect failure is still retried once with a fresh connection.
    /// </summary>
    [Fact]
    public async Task PingAsync_ConnectRefusedThenSucceeds_RetriesOnce()
    {
        await using var server = FakeClamdServer.Start("PONG\0");
        var attempts = 0;
        await using var client = new ClamAVClient(
            new() { Timeout = TimeSpan.FromSeconds(5) },
            async ct =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new SocketException((int)SocketError.ConnectionRefused);
                }

                var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", server.Port, ct);
                return (Stream)tcp.GetStream();
            });

        var result = await client.PingAsync(TestCt);

        Assert.True(result);
        Assert.Equal(2, attempts);
    }

    /// <summary>
    /// Verifies that <see cref="ClamConnectionTimeoutException"/> is a <see cref="ClamConnectionException"/> and preserves its inner exception.
    /// </summary>
    [Fact]
    public void ClamConnectionTimeoutException_WithInner_IsConnectionExceptionAndKeepsInner()
    {
        var inner = new SocketException((int)SocketError.TimedOut);

        var ex = new ClamConnectionTimeoutException("timed out", inner);

        Assert.IsAssignableFrom<ClamConnectionException>(ex);
        Assert.Equal("timed out", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    /// <summary>
    /// Verifies that changing <see cref="ClamClientOptions.Timeout"/> after construction does not affect the validated value in use.
    /// </summary>
    [Fact]
    public async Task PingAsync_TimeoutChangedAfterConstruction_UsesValidatedTimeout()
    {
        await using var server = FakeClamdServer.Start("PONG\0");
        var options = new ClamClientOptions
        {
            Endpoint = ClamEndpoint.Tcp("127.0.0.1", server.Port),
            Timeout = TimeSpan.FromSeconds(5)
        };
        await using var client = new ClamAVClient(options);
        options.Timeout = TimeSpan.Zero;

        var result = await client.PingAsync(TestCt);

        Assert.True(result);
    }

    /// <summary>
    /// Reserves a loopback port by binding and immediately releasing it, so that nothing is listening on it.
    /// </summary>
    /// <returns>A TCP port number on the loopback interface that has no listener.</returns>
    private static int GetUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static ClamAVClient BuildClient(int port) =>
        new(new()
        {
            Endpoint = ClamEndpoint.Tcp("127.0.0.1", port),
            Timeout = TimeSpan.FromSeconds(5)
        });

    /// <summary>
    /// A <see cref="TcpClient"/> that records whether it has been disposed.
    /// </summary>
    private sealed class DisposeTrackingTcpClient : TcpClient
    {
        /// <summary>
        /// Gets a value indicating whether <see cref="TcpClient.Dispose(bool)"/> has been called.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}