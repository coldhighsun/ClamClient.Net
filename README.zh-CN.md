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

[English](README.md) | 简体中文 | [繁體中文](README.zh-TW.md)

</div>

---

适用于 [ClamAV](https://www.clamav.net/)（`clamd`）的异步 .NET 客户端库，支持 TCP 和 Unix 套接字，内置连接池。

## 功能特性

- 通过 `INSTREAM` 扫描文件及任意字节流
- 支持 TCP 和 Unix 域套接字端点
- 完整的 async/await 支持，含 `CancellationToken`
- 内置连接池——复用 IDSESSION 连接，降低每次请求的开销
- Microsoft DI 集成（`AddClamClient`）——将 `IClamClient` 注册为由连接池支撑的单例
- 除 `Microsoft.Extensions.DependencyInjection.Abstractions` 和 `Microsoft.Extensions.Options` 外无额外运行时依赖

## 环境要求

- .NET 8 或更高版本
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

> Unix 域套接字仅支持 Linux 和 macOS；在 Windows 上调用 `ClamEndpoint.UnixSocket` 会抛出 `PlatformNotSupportedException`。

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

### 基准测试

[`benchmarks/ClamClient.Net.Benchmarks`](benchmarks/ClamClient.Net.Benchmarks) 使用 [BenchmarkDotNet](https://benchmarkdotnet.org/) 对真实 clamd 测试（默认 `localhost:3310`，可通过 `CLAMD_HOST` / `CLAMD_PORT` 覆盖）：

```bash
dotnet run -c Release --project benchmarks/ClamClient.Net.Benchmarks -- --filter "*"
```

测试环境：BenchmarkDotNet 0.15.8、Windows 11、Intel Core i9-13980HX、.NET 10.0.15、本机 ClamAV 1.5.4。被扫描的是内存中的小数据流，因此结果主要反映连接与协议开销，而非扫描引擎耗时。

**连接复用**——顺序扫描，共享一个客户端 vs. 每次扫描新建客户端（及 TCP 连接）：

| 方法               | 载荷   | 平均耗时 | 比值 | 内存分配 |
|------------------- |-------:|---------:|-----:|---------:|
| NewClientPerScan   | 1 KB   | 4.97 ms  | 1.00 | 10.51 KB |
| SharedPooledClient | 1 KB   | 1.70 ms  | 0.35 | 1.30 KB  |
| NewClientPerScan   | 256 KB | 6.31 ms  | 1.00 | 10.53 KB |
| SharedPooledClient | 256 KB | 4.13 ms  | 0.66 | 1.30 KB  |

**连接池大小**——32 个并发调用方共享一个客户端，1 KB 载荷，每次扫描的平均耗时与内存分配：

| MaxConnections | 平均耗时 | 内存分配 |
|---------------:|---------:|---------:|
| 1              | 1.90 ms  | 1.46 KB  |
| 2              | 1.25 ms  | 1.46 KB  |
| 5              | 1.17 ms  | 1.45 KB  |
| 10             | 1.10 ms  | 1.44 KB  |
| 0（不限制）    | 1.07 ms  | 1.30 KB  |

复用连接可使小载荷的单次扫描耗时降低约三分之二，256 KB 载荷约降低三分之一，内存分配减少约 88%（分块缓冲区和写入缓冲区均来自 `ArrayPool`，因此分配量不随载荷大小增长）。连接数从 1 增加到 2 时吞吐明显提升，之后趋于平缓，因为瓶颈已转移到 clamd 自身；结果会因硬件、网络和 clamd 的 `MaxThreads` 而异。

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
| `ClamConnectionTimeoutException` | 连接 clamd 超过 `Timeout`（`ClamConnectionException` 的子类，不会自动重试） |
| `ClamProtocolException` | clamd 返回意外或无法解析的响应 |

## 许可证

MIT
