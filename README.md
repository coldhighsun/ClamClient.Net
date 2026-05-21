# ClamClient.Net

Async .NET client library for [ClamAV](https://www.clamav.net/) (`clamd`) with TCP and Unix socket support.

[![CI](https://github.com/coldhighsun/ClamClient.Net/actions/workflows/ci.yml/badge.svg)](https://github.com/coldhighsun/ClamClient.Net/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ClamClient.Net.svg)](https://www.nuget.org/packages/ClamClient.Net)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ClamClient.Net.svg)](https://www.nuget.org/packages/ClamClient.Net)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## Features

- Scan files and arbitrary byte streams via `INSTREAM`
- TCP and Unix domain socket endpoints
- Full async/await with `CancellationToken` support
- Microsoft DI integration (`AddClamClient`)
- No external runtime dependencies beyond `Microsoft.Extensions.DependencyInjection.Abstractions`

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

### Unix domain socket (Linux / macOS)

```csharp
var options = new ClamClientOptions
{
    Endpoint = ClamEndpoint.UnixSocket("/var/run/clamav/clamd.sock"),
};
```

> Unix domain sockets require .NET 5 or later.

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

### `ScanResult`

| Member | Description |
|--------|-------------|
| `Status` | `Clean`, `ThreatFound`, `Error`, or `Unknown` |
| `Threats` | List of `DetectedThreat` (non-empty only when `ThreatFound`) |
| `RawResponse` | Raw string received from clamd |

### Exceptions

| Type | When thrown |
|------|-------------|
| `ClamConnectionException` | Connection cannot be established or is lost |
| `ClamProtocolException` | clamd returns an unexpected or unparseable response |
| `ClamStreamSizeExceededException` | Input stream exceeds `MaxStreamSize` |

## License

MIT

---

# ClamClient.Net（中文说明）

适用于 [ClamAV](https://www.clamav.net/)（`clamd`）的异步 .NET 客户端库，支持 TCP 和 Unix 套接字。

[![CI](https://github.com/coldhighsun/ClamClient.Net/actions/workflows/ci.yml/badge.svg)](https://github.com/coldhighsun/ClamClient.Net/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ClamClient.Net.svg)](https://www.nuget.org/packages/ClamClient.Net)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ClamClient.Net.svg)](https://www.nuget.org/packages/ClamClient.Net)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## 功能特性

- 通过 `INSTREAM` 扫描文件及任意字节流
- 支持 TCP 和 Unix 域套接字端点
- 完整的 async/await 支持，含 `CancellationToken`
- Microsoft DI 集成（`AddClamClient`）
- 除 `Microsoft.Extensions.DependencyInjection.Abstractions` 外无额外运行时依赖

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

### Unix 域套接字（Linux / macOS）

```csharp
var options = new ClamClientOptions
{
    Endpoint = ClamEndpoint.UnixSocket("/var/run/clamav/clamd.sock"),
};
```

> Unix 域套接字需要 .NET 5 或更高版本。

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

### `ScanResult`

| 成员 | 说明 |
|------|------|
| `Status` | `Clean`、`ThreatFound`、`Error` 或 `Unknown` |
| `Threats` | `DetectedThreat` 列表（仅 `ThreatFound` 时非空） |
| `RawResponse` | 从 clamd 收到的原始字符串 |

### 异常

| 类型 | 抛出时机 |
|------|----------|
| `ClamConnectionException` | 无法建立连接或连接断开 |
| `ClamProtocolException` | clamd 返回意外或无法解析的响应 |
| `ClamStreamSizeExceededException` | 输入流超过 `MaxStreamSize` |

## 许可证

MIT
