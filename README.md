# ClamClient.Net

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

---

# ClamClient.Net（中文说明）

适用于 [ClamAV](https://www.clamav.net/)（`clamd`）的异步 .NET 客户端库，支持 TCP 和 Unix 套接字，内置连接池。

[![CI](https://github.com/coldhighsun/ClamClient.Net/actions/workflows/ci.yml/badge.svg)](https://github.com/coldhighsun/ClamClient.Net/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ClamClient.Net.svg)](https://www.nuget.org/packages/ClamClient.Net)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ClamClient.Net.svg)](https://www.nuget.org/packages/ClamClient.Net)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## 功能特性

- 通过 `INSTREAM` 扫描文件及任意字节流
- 支持 TCP 和 Unix 域套接字端点
- 完整的 async/await 支持，含 `CancellationToken`
- 内置连接池——复用 IDSESSION 连接，降低每次请求的开销
- Microsoft DI 集成（`AddClamClient`）——将 `IClamClient` 注册为由连接池支撑的单例
- 除 `Microsoft.Extensions.DependencyInjection.Abstractions` 和 `Microsoft.Extensions.Options` 外无额外运行时依赖

## 环境要求

- .NET Standard 2.0 或 .NET 8+
- 正在运行的 [clamd](https://docs.clamav.net/manual/Usage/Scanning.html#clamd) 守护进程

本地快速启动守护进程：

```bash
docker run -p 3310:3310 clamav/clamav:latest
```

## 安装

```bash
dotnet add package ClamClient.Net
```

## 快速入门

### 直接实例化

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

// 扫描流
using var stream = File.OpenRead("/path/to/upload");
ScanResult result = await client.ScanStreamAsync(stream);

if (result.Status == ScanStatus.ThreatFound)
{
    foreach (var threat in result.Threats)
        Console.WriteLine($"检测到威胁：{threat.ThreatName}");
}
```

### 依赖注入（ASP.NET Core / Generic Host）

```csharp
// Program.cs
builder.Services.AddClamClient(options =>
{
    options.Endpoint = ClamEndpoint.Tcp("localhost", 3310);
    options.Timeout = TimeSpan.FromSeconds(15);
});
```

在需要的地方注入 `IClamClient`：

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

`AddClamClient` 同时注册了 `IOptions<ClamClientOptions>`，如需在运行时读取已解析的选项，可直接注入它。

### Unix 域套接字（Linux / macOS）

```csharp
var options = new ClamClientOptions
{
    Endpoint = ClamEndpoint.UnixSocket("/var/run/clamav/clamd.sock"),
};
```

> Unix 域套接字需要 .NET 5 或更高版本。

## 连接池

`ClamAVClient` 内部维护一个 IDSESSION 连接池。每条连接在打开时发送一次 `zIDSESSION\0` 并保持不关闭，后续命令复用同一连接，从而消除每次扫描的 TCP 握手和 ClamAV 会话建立开销。

**工作原理：**

- 发出命令时，连接池提供一个空闲连接，若无空闲则新建连接。
- 连接使用中时，占用 `MaxConnections` 计数中的一个槽位。
- 调用返回后，连接归还连接池，供下一个调用方复用。
- 所有槽位均已占用时，后续调用方等待，直到有连接释放（受操作的 `CancellationToken` 控制）。
- 空闲时长超过 `IdleConnectionTimeout` 的连接会被自动驱逐并关闭。

**调优建议：**

| 场景 | 建议 |
|------|------|
| 低流量服务 | 保留默认值（`MaxConnections = 10`，`IdleConnectionTimeout = 30 s`） |
| 高吞吐上传服务 | 将 `MaxConnections` 调高至与预期并发数匹配 |
| 短生命周期进程 / 批处理任务 | 降低 `IdleConnectionTimeout`，使连接在突发流量后尽快关闭 |
| 不限连接数 | 设置 `MaxConnections = 0` |

应用退出时请 Dispose `ClamAVClient`（或 DI 容器）——这会向所有空闲连接发送 `zEND\0` 并干净地关闭连接池。

## API 参考

### `IClamClient`

| 方法 | 说明 |
|------|------|
| `PingAsync` | 若 clamd 回复 `PONG` 则返回 `true` |
| `GetVersionAsync` | 返回 clamd 版本字符串 |
| `GetStatsAsync` | 返回原始 `STATS` 信息块 |
| `ScanFileAsync(filePath)` | 扫描 clamd 进程可访问的文件路径 |
| `MultiScanAsync(filePath)` | 多线程目录/文件扫描 |
| `ScanStreamAsync(stream)` | 通过 `INSTREAM` 将任意数据流式传输到 clamd |
| `ReloadAsync` | 重新加载病毒数据库 |
| `ShutdownAsync` | 终止 clamd 进程 |

### `ClamClientOptions`

| 属性 | 默认值 | 说明 |
|------|--------|------|
| `Endpoint` | `tcp:localhost:3310` | 目标 clamd 端点 |
| `Timeout` | `00:00:10` | 套接字连接及读取超时 |
| `ChunkSize` | `131072`（128 KB） | `INSTREAM` 每块最大字节数 |
| `MaxStreamSize` | `26214400`（25 MB） | `INSTREAM` 总负载硬性上限 |
| `MaxConnections` | `10` | 最大连接池连接数（0 表示不限制） |
| `IdleConnectionTimeout` | `00:00:30` | 空闲连接在被回收前的最长保留时间 |

### `ScanResult`

| 成员 | 说明 |
|------|------|
| `Status` | `Clean`、`ThreatFound`、`Error` 或 `StreamTooLarge` |
| `Threats` | `DetectedThreat` 列表（仅 `ThreatFound` 时非空） |
| `RawResponse` | 从 clamd 收到的原始字符串 |

`StreamTooLarge` 由 `ScanStreamAsync` 在流超过 `MaxStreamSize` 时返回，此时不会向 clamd 发送任何数据。

### 异常

| 类型 | 抛出时机 |
|------|----------|
| `ClamConnectionException` | 无法建立连接或连接断开 |
| `ClamProtocolException` | clamd 返回意外或无法解析的响应 |

## 许可证

MIT
