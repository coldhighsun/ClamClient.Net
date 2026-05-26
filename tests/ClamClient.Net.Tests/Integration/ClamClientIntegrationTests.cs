using ClamClient.Net.Configuration;
using ClamClient.Net.Results;
using ClamClient.Net.Tests.Fakes;
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
        var sentText = Encoding.ASCII.GetString(server.ReceivedBytes.Take(10).ToArray());
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

    private static ClamAVClient BuildClient(int port) =>
        new(new ClamClientOptions
        {
            Endpoint = ClamEndpoint.Tcp("127.0.0.1", port),
            Timeout = TimeSpan.FromSeconds(5)
        });
}