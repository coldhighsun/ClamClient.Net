namespace ClamClient.Net.Configuration;

/// <summary>
/// Represents the network endpoint of a clamd daemon.
/// </summary>
public sealed class ClamEndpoint
{
    /// <summary>
    /// Private constructor. Use the static factory methods <see cref="Tcp"/> and <see cref="UnixSocket"/> to create instances.
    /// </summary>
    /// <param name="host">The hostname or IP address for a TCP connection.</param>
    /// <param name="port">The TCP port number.</param>
    private ClamEndpoint(string host, int port)
    {
        Host = host;
        Port = port;
    }

    /// <summary>
    /// Initializes a new instance of the ClamEndpoint class with the specified Unix socket path.
    /// </summary>
    /// <param name="socketPath">The path to the Unix domain socket.</param>
    private ClamEndpoint(string socketPath)
    {
        UnixSocketPath = socketPath;
    }

    /// <summary>
    /// The hostname or IP address for a TCP connection. <see langword="null"/> for Unix socket endpoints.
    /// </summary>
    public string? Host
    {
        get;
    }

    /// <summary>
    /// <see langword="true"/> when this endpoint uses a Unix domain socket rather than TCP.
    /// </summary>
    public bool IsUnixSocket => UnixSocketPath is not null;

    /// <summary>
    /// The TCP port number. Meaningless for Unix socket endpoints.
    /// </summary>
    public int Port
    {
        get;
    }

    /// <summary>
    /// The file system path for a Unix domain socket connection. <see langword="null"/> for TCP endpoints.
    /// </summary>
    public string? UnixSocketPath
    {
        get;
    }

    /// <summary>
    /// Creates a TCP endpoint. Port defaults to 3310.
    /// </summary>
    public static ClamEndpoint Tcp(string host, int port = 3310) => new(host, port);

    /// <summary>
    /// Creates a Unix domain socket endpoint. Only supported on Linux and macOS.
    /// </summary>
    public static ClamEndpoint UnixSocket(string path)
    {
#if NET5_0_OR_GREATER
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Unix domain sockets are only supported on Linux and macOS.");
        }
#else
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux) &&
            !System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            throw new PlatformNotSupportedException("Unix domain sockets are only supported on Linux and macOS.");
        }
#endif
        return new(path);
    }

    /// <summary>
    /// Returns a human-readable representation of the endpoint, e.g. <c>tcp:localhost:3310</c> or <c>unix:/var/run/clamav/clamd.sock</c>.
    /// </summary>
    public override string ToString() =>
        IsUnixSocket ? $"unix:{UnixSocketPath}" : $"tcp:{Host}:{Port}";
}