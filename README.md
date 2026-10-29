# ClamClient.Net

**English** | [简体中文](https://github.com/coldhighsun/ClamClient.Net/blob/main/README.zh-CN.md)

Async .NET client library for [ClamAV](https://www.clamav.net/) (`clamd`) with TCP and Unix socket support, and built-in connection pooling.

[![CI](https://github.com/coldhighsun/ClamClient.Net/actions/workflows/ci.yml/badge.svg)](https://github.com/coldhighsun/ClamClient.Net/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ClamClient.Net.svg)](https://www.nuget.org/packages/ClamClient.Net)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ClamClient.Net.svg)](https://www.nuget.org/packages/ClamClient.Net)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## Features

- Scan files and arbitrary byte streams via `INSTREAM`
- TCP and Unix domain socket endpoints
- Full async/await with `CancellationToken` support
- Built-in connection pool — reuses IDSESSION connections to reduce per-request overhead
- Microsoft DI integration (`AddClamClient`) — registers `IClamClient` as a singleton backed by the pool
- No external runtime dependencies beyond `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Options`

## Requirements

- .NET Standard 2.0 or .NET 8+
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

> Unix domain sockets require .NET 5 or later.

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
| `ClamProtocolException` | clamd returns an unexpected or unparseable response |

## License

MIT
