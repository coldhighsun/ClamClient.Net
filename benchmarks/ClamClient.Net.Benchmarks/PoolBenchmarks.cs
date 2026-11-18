using BenchmarkDotNet.Attributes;
using ClamClient.Net;

namespace ClamClient.Net.Benchmarks;

/// <summary>
/// Compares sequential scans through a shared pooled client against creating a new client (and connection) per scan.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ConnectionReuseBenchmarks
{
    /// <summary>
    /// Payload bytes scanned by every benchmark invocation.
    /// </summary>
    private byte[] _payload = [];

    /// <summary>
    /// Client shared by the pooled benchmark.
    /// </summary>
    private ClamAVClient _shared = null!;

    /// <summary>
    /// Gets or sets the payload size in kilobytes.
    /// </summary>
    [Params(1, 256)]
    public int PayloadKb { get; set; }

    /// <summary>
    /// Creates the shared client and opens its pooled connection before measurement starts.
    /// </summary>
    /// <returns>A task that completes once the client is warmed up.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _payload = BenchmarkEnvironment.CreatePayload(PayloadKb);
        _shared = BenchmarkEnvironment.CreateClient(maxConnections: 1);
        await BenchmarkEnvironment.EnsureReachableAsync(_shared).ConfigureAwait(false);
        await BenchmarkEnvironment.ScanCleanAsync(_shared, _payload).ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the shared client, which closes its pooled connection.
    /// </summary>
    /// <returns>A task that completes once the client is disposed.</returns>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _shared.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Baseline: opens a new client and connection for every scan, so nothing is reused.
    /// </summary>
    /// <returns>A task that completes once the scan has finished.</returns>
    [Benchmark(Baseline = true)]
    public async Task NewClientPerScan()
    {
        await using var client = BenchmarkEnvironment.CreateClient(maxConnections: 1);
        await BenchmarkEnvironment.ScanCleanAsync(client, _payload).ConfigureAwait(false);
    }

    /// <summary>
    /// Scans through the shared client, reusing its pooled IDSESSION connection.
    /// </summary>
    /// <returns>A task that completes once the scan has finished.</returns>
    [Benchmark]
    public Task SharedPooledClient()
    {
        return BenchmarkEnvironment.ScanCleanAsync(_shared, _payload);
    }
}

/// <summary>
/// Measures how the <c>MaxConnections</c> pool size affects throughput when many callers scan concurrently.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class PoolSizeBenchmarks
{
    /// <summary>
    /// Number of scans issued per benchmark invocation; results are reported per scan.
    /// </summary>
    private const int ScansPerInvoke = 64;

    /// <summary>
    /// Number of concurrent callers sharing the client.
    /// </summary>
    private const int Workers = 32;

    /// <summary>
    /// Payload bytes scanned by every scan.
    /// </summary>
    private byte[] _payload = [];

    /// <summary>
    /// Client under test, created with the parameterised pool size.
    /// </summary>
    private ClamAVClient _client = null!;

    /// <summary>
    /// Gets or sets the pool size under test; 0 means unlimited.
    /// </summary>
    [Params(1, 2, 5, 10, 0)]
    public int MaxConnections { get; set; }

    /// <summary>
    /// Creates the client and opens a first connection before measurement starts.
    /// </summary>
    /// <returns>A task that completes once the client is warmed up.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _payload = BenchmarkEnvironment.CreatePayload(kilobytes: 1);
        _client = BenchmarkEnvironment.CreateClient(MaxConnections);
        await BenchmarkEnvironment.EnsureReachableAsync(_client).ConfigureAwait(false);
        await BenchmarkEnvironment.ScanCleanAsync(_client, _payload).ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the client, which closes all pooled connections.
    /// </summary>
    /// <returns>A task that completes once the client is disposed.</returns>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Issues <see cref="ScansPerInvoke"/> scans spread over <see cref="Workers"/> concurrent callers.
    /// </summary>
    /// <returns>A task that completes once all scans have finished.</returns>
    [Benchmark(OperationsPerInvoke = ScansPerInvoke)]
    public async Task ConcurrentScans()
    {
        var next = -1;
        var workers = Enumerable.Range(0, Workers).Select(async _ =>
        {
            while (Interlocked.Increment(ref next) < ScansPerInvoke)
            {
                await BenchmarkEnvironment.ScanCleanAsync(_client, _payload).ConfigureAwait(false);
            }
        });

        await Task.WhenAll(workers).ConfigureAwait(false);
    }
}
