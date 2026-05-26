using ClamClient.Net.Configuration;

namespace ClamClient.Net.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="ClamEndpoint"/> factory methods, properties, and formatting.
/// </summary>
public sealed class ClamEndpointTests
{
    /// <summary>
    /// Verifies that <see cref="ClamEndpoint.Tcp"/> uses the supplied custom port.
    /// </summary>
    [Fact]
    public void Tcp_CustomPort_UsesGivenPort()
    {
        var ep = ClamEndpoint.Tcp("localhost", 9999);

        Assert.Equal(9999, ep.Port);
    }

    /// <summary>
    /// Verifies that the default port for <see cref="ClamEndpoint.Tcp"/> is 3310.
    /// </summary>
    [Fact]
    public void Tcp_DefaultPort_Is3310()
    {
        var ep = ClamEndpoint.Tcp("localhost");

        Assert.Equal(3310, ep.Port);
    }

    /// <summary>
    /// Verifies that a TCP endpoint reports <see cref="ClamEndpoint.IsUnixSocket"/> as false.
    /// </summary>
    [Fact]
    public void Tcp_IsUnixSocket_ReturnsFalse()
    {
        var ep = ClamEndpoint.Tcp("localhost");

        Assert.False(ep.IsUnixSocket);
        Assert.Null(ep.UnixSocketPath);
    }

    /// <summary>
    /// Verifies that <see cref="ClamEndpoint.Tcp"/> stores the supplied host.
    /// </summary>
    [Fact]
    public void Tcp_SetsHost()
    {
        var ep = ClamEndpoint.Tcp("clamd.internal");

        Assert.Equal("clamd.internal", ep.Host);
    }

    /// <summary>
    /// Verifies that <see cref="ClamEndpoint.ToString"/> returns the expected <c>tcp:host:port</c> format.
    /// </summary>
    [Fact]
    public void Tcp_ToString_ReturnsTcpFormat()
    {
        var ep = ClamEndpoint.Tcp("clamd.internal", 3310);

        Assert.Equal("tcp:clamd.internal:3310", ep.ToString());
    }

    /// <summary>
    /// Verifies that a Unix socket endpoint reports <see cref="ClamEndpoint.IsUnixSocket"/> as true
    /// and stores the socket path. Only runs on Linux/macOS.
    /// </summary>
    [Fact]
    public void UnixSocket_IsUnixSocket_ReturnsTrue()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return; // Unix sockets not supported on this platform
        }

        var ep = ClamEndpoint.UnixSocket("/var/run/clamav/clamd.sock");

        Assert.True(ep.IsUnixSocket);
        Assert.Equal("/var/run/clamav/clamd.sock", ep.UnixSocketPath);
        Assert.Null(ep.Host);
    }

    /// <summary>
    /// Verifies that <see cref="ClamEndpoint.UnixSocket"/> throws <see cref="PlatformNotSupportedException"/>
    /// on platforms other than Linux and macOS (e.g. Windows).
    /// </summary>
    [Fact]
    public void UnixSocket_OnNonUnixPlatform_ThrowsPlatformNotSupportedException()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return; // only meaningful on non-Unix platforms
        }

        Assert.Throws<PlatformNotSupportedException>(
            () => ClamEndpoint.UnixSocket("/var/run/clamav/clamd.sock"));
    }

    /// <summary>
    /// Verifies that <see cref="ClamEndpoint.ToString"/> returns the expected <c>unix:path</c> format.
    /// Only runs on Linux/macOS.
    /// </summary>
    [Fact]
    public void UnixSocket_ToString_ReturnsUnixFormat()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return; // Unix sockets not supported on this platform
        }

        var ep = ClamEndpoint.UnixSocket("/var/run/clamav/clamd.sock");

        Assert.Equal("unix:/var/run/clamav/clamd.sock", ep.ToString());
    }
}