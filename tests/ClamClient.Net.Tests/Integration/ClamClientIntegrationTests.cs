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
    public async Task GetVersionAsync_ReturnsVersionString()
    {
        const string version = "ClamAV 1.0.0/26800/Mon Oct 30 08:59:00 2023";
        await using var server = FakeClamdServer.Start(version + "\0");
        var client = BuildClient(server.Port);

        var result = await client.GetVersionAsync(TestCt);

        Assert.Equal(version, result);
    }

    [Fact]
    public async Task PingAsync_ReturnsTrueWhenClamdRepliesPong()
    {
        await using var server = FakeClamdServer.Start("PONG\0");
        var client = BuildClient(server.Port);

        var result = await client.PingAsync(TestCt);

        Assert.True(result);
    }

    [Fact]
    public async Task PingAsync_SendsCorrectCommand()
    {
        await using var server = FakeClamdServer.Start("PONG\0");
        var client = BuildClient(server.Port);

        await client.PingAsync(TestCt);

        var sentText = Encoding.ASCII.GetString(server.ReceivedBytes);
        Assert.Equal("zPING\0", sentText);
    }

    [Fact]
    public async Task ScanFileAsync_CleanFile_ReturnsCleanResult()
    {
        await using var server = FakeClamdServer.Start("/tmp/test.txt: OK\0");
        var client = BuildClient(server.Port);

        var result = await client.ScanFileAsync("/tmp/test.txt", TestCt);

        Assert.Equal(ScanStatus.Clean, result.Status);
    }

    [Fact]
    public async Task ScanFileAsync_InfectedFile_ReturnsThreatFoundResult()
    {
        await using var server = FakeClamdServer.Start("/tmp/virus.exe: Eicar-Test-Signature FOUND\0");
        var client = BuildClient(server.Port);

        var result = await client.ScanFileAsync("/tmp/virus.exe", TestCt);

        Assert.Equal(ScanStatus.ThreatFound, result.Status);
        Assert.Single(result.Threats);
        Assert.Equal("Eicar-Test-Signature", result.Threats[0].ThreatName);
    }

    [Fact]
    public async Task ScanStreamAsync_CleanData_ReturnsCleanResult()
    {
        await using var server = FakeClamdServer.Start("stream: OK\0");
        var client = BuildClient(server.Port);
        var data = new MemoryStream(Encoding.UTF8.GetBytes("Hello, world!"));

        var result = await client.ScanStreamAsync(data, TestCt);

        Assert.Equal(ScanStatus.Clean, result.Status);
    }

    [Fact]
    public async Task ScanStreamAsync_InfectedData_ReturnsThreatFoundResult()
    {
        await using var server = FakeClamdServer.Start("stream: Eicar-Test-Signature FOUND\0");
        var client = BuildClient(server.Port);
        var data = new MemoryStream(Encoding.UTF8.GetBytes("X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"));

        var result = await client.ScanStreamAsync(data, TestCt);

        Assert.Equal(ScanStatus.ThreatFound, result.Status);
        Assert.Equal("Eicar-Test-Signature", result.Threats[0].ThreatName);
    }

    [Fact]
    public async Task ScanStreamAsync_SendsInstreamCommandWithCorrectFraming()
    {
        await using var server = FakeClamdServer.Start("stream: OK\0");
        var client = BuildClient(server.Port);
        var data = new MemoryStream("test"u8.ToArray());

        await client.ScanStreamAsync(data, TestCt);

        // Command starts with "zINSTREAM\0"
        var sentText = Encoding.ASCII.GetString(server.ReceivedBytes.Take(10).ToArray());
        Assert.Equal("zINSTREAM\0", sentText);
    }

    private static ClamAVClient BuildClient(int port) =>
        new(new()
        {
            Endpoint = ClamEndpoint.Tcp("127.0.0.1", port),
            Timeout = TimeSpan.FromSeconds(5)
        });
}