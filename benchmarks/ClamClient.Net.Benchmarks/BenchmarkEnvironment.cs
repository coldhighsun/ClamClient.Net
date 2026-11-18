using ClamClient.Net;
using ClamClient.Net.Configuration;

namespace ClamClient.Net.Benchmarks;

/// <summary>
/// Shared helpers for creating clients and payloads against the benchmark clamd endpoint.
/// </summary>
internal static class BenchmarkEnvironment
{
    /// <summary>
    /// Creates a client for the configured clamd endpoint with the supplied pool size.
    /// </summary>
    /// <param name="maxConnections">Pool cap; 0 means unlimited.</param>
    /// <returns>A new client; the caller must dispose it.</returns>
    internal static ClamAVClient CreateClient(int maxConnections)
    {
        var host = Environment.GetEnvironmentVariable("CLAMD_HOST") ?? "localhost";
        var port = int.TryParse(Environment.GetEnvironmentVariable("CLAMD_PORT"), out var p) ? p : 3310;

        return new ClamAVClient(new ClamClientOptions
        {
            Endpoint = ClamEndpoint.Tcp(host, port),
            MaxConnections = maxConnections,
            Timeout = TimeSpan.FromSeconds(30),
        });
    }

    /// <summary>
    /// Creates a harmless payload of the requested size.
    /// </summary>
    /// <param name="kilobytes">Payload size in kilobytes.</param>
    /// <returns>The payload bytes.</returns>
    internal static byte[] CreatePayload(int kilobytes)
    {
        var payload = new byte[kilobytes * 1024];
        Array.Fill(payload, (byte)'a');
        return payload;
    }

    /// <summary>
    /// Verifies that clamd answers a PING and throws a descriptive exception otherwise.
    /// </summary>
    /// <param name="client">The client to probe with.</param>
    /// <returns>A task that completes once clamd has answered.</returns>
    internal static async Task EnsureReachableAsync(ClamAVClient client)
    {
        try
        {
            if (await client.PingAsync().ConfigureAwait(false))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Cannot reach clamd. Start one with: docker run -p 3310:3310 clamav/clamav:latest " +
                "(or set CLAMD_HOST / CLAMD_PORT).", ex);
        }

        throw new InvalidOperationException("clamd did not answer PING.");
    }

    /// <summary>
    /// Scans the payload and throws if the result is not clean, so failed scans are never timed as successes.
    /// </summary>
    /// <param name="client">The client to scan with.</param>
    /// <param name="payload">The bytes to scan.</param>
    /// <returns>A task that completes once the scan has finished.</returns>
    internal static async Task ScanCleanAsync(ClamAVClient client, byte[] payload)
    {
        using var stream = new MemoryStream(payload);
        var result = await client.ScanStreamAsync(stream).ConfigureAwait(false);
        if (result.Status != ClamClient.Net.Results.ScanStatus.Clean)
        {
            throw new InvalidOperationException($"Unexpected scan result: {result.Status}.");
        }
    }
}
