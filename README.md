<div align="center">

# ClamClient.Net

[![CI](https://github.com/coldhighsun/ClamClient.Net/actions/workflows/ci.yml/badge.svg)](https://github.com/coldhighsun/ClamClient.Net/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ClamClient.Net.svg)](https://www.nuget.org/packages/ClamClient.Net)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ClamClient.Net.svg)](https://www.nuget.org/packages/ClamClient.Net)
[![Latest Release](https://img.shields.io/github/v/release/coldhighsun/ClamClient.Net?sort=semver)](https://github.com/coldhighsun/ClamClient.Net/releases/latest)
[![Latest Pre-release](https://img.shields.io/github/v/release/coldhighsun/ClamClient.Net?include_prereleases&sort=semver&label=pre-release)](https://github.com/coldhighsun/ClamClient.Net/releases)
[![Open Issues](https://img.shields.io/github/issues/coldhighsun/ClamClient.Net)](https://github.com/coldhighsun/ClamClient.Net/issues)
[![Open PRs](https://img.shields.io/github/issues-pr/coldhighsun/ClamClient.Net)](https://github.com/coldhighsun/ClamClient.Net/pulls)
[![Last Commit](https://img.shields.io/github/last-commit/coldhighsun/ClamClient.Net)](https://github.com/coldhighsun/ClamClient.Net/commits/main)
[![GitHub Stars](https://img.shields.io/github/stars/coldhighsun/ClamClient.Net?style=flat)](https://github.com/coldhighsun/ClamClient.Net/stargazers)
[![License: MIT](https://img.shields.io/github/license/coldhighsun/ClamClient.Net)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%2010-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download)

English | [简体中文](https://github.com/coldhighsun/ClamClient.Net/blob/main/README.zh-CN.md) | [繁體中文](https://github.com/coldhighsun/ClamClient.Net/blob/main/README.zh-TW.md)

</div>

---

Async .NET client library for [ClamAV](https://www.clamav.net/) (`clamd`) with TCP and Unix socket support, and built-in connection pooling.

## Features

- Scan files and arbitrary byte streams via `INSTREAM`
- TCP and Unix domain socket endpoints
- Full async/await with `CancellationToken` support
- Built-in connection pool — reuses IDSESSION connections to reduce per-request overhead
- Microsoft DI integration (`AddClamClient`) — registers `IClamClient` as a singleton backed by the pool
- No external runtime dependencies beyond `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Options`

## Requirements

- .NET 8 or later
- A running [clamd](https://docs.clamav.net/manual/Usage/Scanning.html#clamd) daemon

Quick way to get a daemon running locally:

```bash
docker run -p 3310:3310 clamav/clamav:latest
```

## Installation

```bash
dotnet add package ClamClient.Net
```

## Quick start

### Direct instantiation

```csharp
using ClamClient.Net;
using ClamClient.Net.Configuration;
using ClamClient.Net.Results;

var options = new ClamClientOptions
{
    Endpoint = ClamEndpoint.Tcp("localhost", 3310),
};

var client = new ClamAVClient(options);

bool alive = await client.PingAsync();
string version = await client.GetVersionAsync();

// Scan a stream
using var stream = File.OpenRead("/path/to/upload");
ScanResult result = await client.ScanStreamAsync(stream);

if (result.Status == ScanStatus.ThreatFound)
{
    foreach (var threat in result.Threats)
        Console.WriteLine($"Threat detected: {threat.ThreatName}");
}
```

### Dependency Injection (ASP.NET Core / Generic Host)

```csharp
// Program.cs
builder.Services.AddClamClient(options =>
{
    options.Endpoint = ClamEndpoint.Tcp("localhost", 3310);
    options.Timeout = TimeSpan.FromSeconds(15);
});
```

Inject `IClamClient` wherever you need it:

```csharp
public class UploadService(IClamClient clamClient)
{
    public async Task<bool> IsSafeAsync(Stream file, CancellationToken ct = default)
    {
        var result = await clamClient.ScanStreamAsync(file, ct);
        return result.Status == ScanStatus.Clean;
    }
}
```

`AddClamClient` also registers `IOptions<ClamClientOptions>`, so you can inject it directly if you need to read the resolved options at runtime.

### Unix domain socket (Linux / macOS)

```csharp
var options = new ClamClientOptions
{
    Endpoint = ClamEndpoint.UnixSocket("/var/run/clamav/clamd.sock"),
};
```

> Unix domain sockets are only supported on Linux and macOS; `ClamEndpoint.UnixSocket` throws `PlatformNotSupportedException` on Windows.

## Connection pool

`ClamAVClient` owns an internal pool of IDSESSION connections. Each connection sends `zIDSESSION\0` once on open and remains open for subsequent commands, eliminating the TCP handshake and ClamAV session-setup cost on every scan.

**How it works:**

- When a command is issued, the pool hands out an idle connection or opens a new one.
- While a connection is in use, it occupies one slot toward `MaxConnections`.
- When the call returns, the connection is checked back in and reused by the next caller.
- When all slots are occupied, further callers wait until a connection becomes available (subject to the operation's `CancellationToken`).
- Connections idle for longer than `IdleConnectionTimeout` are evicted and closed automatically.

**Tuning:**

| Scenario | Recommendation |
|----------|----------------|
| Low-traffic service | Leave defaults (`MaxConnections = 10`, `IdleConnectionTimeout = 30 s`) |
| High-throughput upload service | Raise `MaxConnections` to match expected concurrency |
| Short-lived process / batch job | Lower `IdleConnectionTimeout` so connections close promptly after the burst |
| Unlimited connections | Set `MaxConnections = 0` |

Dispose `ClamAVClient` (or the DI container) when the application exits — this sends `zEND\0` on every idle connection and closes the pool cleanly.

### Benchmarks

[`benchmarks/ClamClient.Net.Benchmarks`](benchmarks/ClamClient.Net.Benchmarks) uses [BenchmarkDotNet](https://benchmarkdotnet.org/) against a live clamd (default `localhost:3310`, override with `CLAMD_HOST` / `CLAMD_PORT`):

```bash
dotnet run -c Release --project benchmarks/ClamClient.Net.Benchmarks -- --filter "*"
```

Environment: BenchmarkDotNet 0.15.8, Windows 11, Intel Core i9-13980HX, .NET 10.0.15, ClamAV 1.5.4 on `localhost`. Scanned payloads are small in-memory streams, so the numbers mostly reflect connection and protocol overhead rather than scan-engine time.

**Connection reuse** — sequential scans, one shared client vs. a new client (and TCP connection) per scan:

| Method             | Payload | Mean    | Ratio | Allocated |
|------------------- |--------:|--------:|------:|----------:|
| NewClientPerScan   | 1 KB    | 4.97 ms | 1.00  | 10.51 KB  |
| SharedPooledClient | 1 KB    | 1.70 ms | 0.35  | 1.30 KB   |
| NewClientPerScan   | 256 KB  | 6.31 ms | 1.00  | 10.53 KB  |
| SharedPooledClient | 256 KB  | 4.13 ms | 0.66  | 1.30 KB   |

**Pool size** — 32 concurrent callers sharing one client, 1 KB payload, mean time and allocation per scan:

| MaxConnections | Mean    | Allocated |
|---------------:|--------:|----------:|
| 1              | 1.90 ms | 1.46 KB   |
| 2              | 1.25 ms | 1.46 KB   |
| 5              | 1.17 ms | 1.45 KB   |
| 10             | 1.10 ms | 1.44 KB   |
| 0 (unlimited)  | 1.07 ms | 1.30 KB   |

Reusing connections cuts per-scan time by about two thirds for small payloads and by about a third for 256 KB, and cuts allocations by roughly 88% (chunk and write buffers come from `ArrayPool`, so allocation does not grow with payload size). Throughput improves going from one to two connections and then flattens, because clamd itself becomes the bottleneck; results vary with hardware, network and clamd's `MaxThreads`.

## API reference

### `IClamClient`

| Method | Description |
|--------|-------------|
| `PingAsync` | Returns `true` if clamd replies `PONG` |
| `GetVersionAsync` | Returns the clamd version string |
| `GetStatsAsync` | Returns the raw `STATS` block |
| `ScanFileAsync(filePath)` | Scans a file path accessible to the clamd process |
| `MultiScanAsync(filePath)` | Multi-threaded directory/file scan |
| `ScanStreamAsync(stream)` | Streams arbitrary data to clamd via `INSTREAM` |
| `ReloadAsync` | Reloads the virus database |
| `ShutdownAsync` | Terminates the clamd process |

### `ClamClientOptions`

| Property | Default | Description |
|----------|---------|-------------|
| `Endpoint` | `tcp:localhost:3310` | Target clamd endpoint |
| `Timeout` | `00:00:10` | Socket connect and read timeout |
| `ChunkSize` | `131072` (128 KB) | Max bytes per `INSTREAM` chunk |
| `MaxStreamSize` | `26214400` (25 MB) | Hard cap on total `INSTREAM` payload |
| `MaxConnections` | `10` | Maximum pooled connections (0 = unlimited) |
| `IdleConnectionTimeout` | `00:00:30` | How long an idle connection is kept before eviction |

### `ScanResult`

| Member | Description |
|--------|-------------|
| `Status` | `Clean`, `ThreatFound`, `Error`, or `StreamTooLarge` |
| `Threats` | List of `DetectedThreat` (non-empty only when `ThreatFound`) |
| `RawResponse` | Raw string received from clamd |

`StreamTooLarge` is returned by `ScanStreamAsync` when the stream exceeds `MaxStreamSize`. No data is sent to clamd in this case.

### Exceptions

| Type | When thrown |
|------|-------------|
| `ClamConnectionException` | Connection cannot be established or is lost |
| `ClamConnectionTimeoutException` | Connecting to clamd exceeds `Timeout` (subclass of `ClamConnectionException`; not retried automatically) |
| `ClamProtocolException` | clamd returns an unexpected or unparseable response |

## License

MIT
