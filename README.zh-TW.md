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

[English](README.md) | [简体中文](README.zh-CN.md) | 繁體中文

</div>

---

適用於 [ClamAV](https://www.clamav.net/)（`clamd`）的非同步 .NET 用戶端程式庫，支援 TCP 和 Unix 通訊端，內建連線池。

## 功能特色

- 透過 `INSTREAM` 掃描檔案及任意位元組串流
- 支援 TCP 和 Unix 網域通訊端（socket）端點
- 完整的 async/await 支援，含 `CancellationToken`
- 內建連線池——重複使用 IDSESSION 連線，降低每次請求的負擔
- Microsoft DI 整合（`AddClamClient`）——將 `IClamClient` 註冊為由連線池支撐的單例
- 除 `Microsoft.Extensions.DependencyInjection.Abstractions` 和 `Microsoft.Extensions.Options` 外，無其他額外的執行階段相依項目

## 環境需求

- .NET 8 或更新版本
- 正在執行的 [clamd](https://docs.clamav.net/manual/Usage/Scanning.html#clamd) 常駐程式

在本機快速啟動常駐程式：

```bash
docker run -p 3310:3310 clamav/clamav:latest
```

## 安裝

```bash
dotnet add package ClamClient.Net
```

## 快速入門

### 直接建立執行個體

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

// 掃描串流
using var stream = File.OpenRead("/path/to/upload");
ScanResult result = await client.ScanStreamAsync(stream);

if (result.Status == ScanStatus.ThreatFound)
{
    foreach (var threat in result.Threats)
        Console.WriteLine($"偵測到威脅：{threat.ThreatName}");
}
```

### 相依性注入（ASP.NET Core / Generic Host）

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

`AddClamClient` 同時註冊了 `IOptions<ClamClientOptions>`，如需在執行階段讀取已解析的選項，可直接注入它。

### Unix 網域通訊端（Linux / macOS）

```csharp
var options = new ClamClientOptions
{
    Endpoint = ClamEndpoint.UnixSocket("/var/run/clamav/clamd.sock"),
};
```

> Unix 網域通訊端僅支援 Linux 和 macOS；在 Windows 上呼叫 `ClamEndpoint.UnixSocket` 會擲出 `PlatformNotSupportedException`。

## 連線池

`ClamAVClient` 內部維護一個 IDSESSION 連線池。每條連線在開啟時傳送一次 `zIDSESSION\0` 並保持不關閉，後續命令重複使用同一條連線，從而消除每次掃描的 TCP 交握與 ClamAV 工作階段建立的負擔。

**運作方式：**

- 發出命令時，連線池提供一條閒置連線，若無閒置連線則建立新連線。
- 連線使用中時，佔用 `MaxConnections` 計數中的一個名額。
- 呼叫回傳後，連線歸還連線池，供下一個呼叫端重複使用。
- 所有名額皆已被佔用時，後續呼叫端會等待，直到有連線釋出（受該操作的 `CancellationToken` 控制）。
- 閒置時間超過 `IdleConnectionTimeout` 的連線會被自動逐出並關閉。

**調校建議：**

| 情境 | 建議 |
|------|------|
| 低流量服務 | 保留預設值（`MaxConnections = 10`，`IdleConnectionTimeout = 30 s`） |
| 高吞吐量上傳服務 | 將 `MaxConnections` 調高至與預期並行數相符 |
| 短生命週期處理序 / 批次作業 | 降低 `IdleConnectionTimeout`，使連線在突發流量後盡快關閉 |
| 不限連線數 | 設定 `MaxConnections = 0` |

應用程式結束時請 Dispose `ClamAVClient`（或 DI 容器）——這會向所有閒置連線傳送 `zEND\0` 並乾淨地關閉連線池。

## API 參考

### `IClamClient`

| 方法 | 說明 |
|------|------|
| `PingAsync` | 若 clamd 回覆 `PONG` 則回傳 `true` |
| `GetVersionAsync` | 回傳 clamd 版本字串 |
| `GetStatsAsync` | 回傳原始 `STATS` 資訊區塊 |
| `ScanFileAsync(filePath)` | 掃描 clamd 處理序可存取的檔案路徑 |
| `MultiScanAsync(filePath)` | 多執行緒目錄/檔案掃描 |
| `ScanStreamAsync(stream)` | 透過 `INSTREAM` 將任意資料以串流方式傳送至 clamd |
| `ReloadAsync` | 重新載入病毒資料庫 |
| `ShutdownAsync` | 終止 clamd 處理序 |

### `ClamClientOptions`

| 屬性 | 預設值 | 說明 |
|------|--------|------|
| `Endpoint` | `tcp:localhost:3310` | 目標 clamd 端點 |
| `Timeout` | `00:00:10` | 通訊端連線及讀取逾時 |
| `ChunkSize` | `131072`（128 KB） | `INSTREAM` 每個區塊的最大位元組數 |
| `MaxStreamSize` | `26214400`（25 MB） | `INSTREAM` 總負載的硬性上限 |
| `MaxConnections` | `10` | 連線池最大連線數（0 表示不限制） |
| `IdleConnectionTimeout` | `00:00:30` | 閒置連線在被回收前的最長保留時間 |

### `ScanResult`

| 成員 | 說明 |
|------|------|
| `Status` | `Clean`、`ThreatFound`、`Error` 或 `StreamTooLarge` |
| `Threats` | `DetectedThreat` 清單（僅 `ThreatFound` 時非空） |
| `RawResponse` | 從 clamd 收到的原始字串 |

`StreamTooLarge` 由 `ScanStreamAsync` 在串流超過 `MaxStreamSize` 時回傳，此時不會向 clamd 傳送任何資料。

### 例外

| 型別 | 擲回時機 |
|------|----------|
| `ClamConnectionException` | 無法建立連線或連線中斷 |
| `ClamConnectionTimeoutException` | 連線 clamd 超過 `Timeout`（`ClamConnectionException` 的子類別，不會自動重試） |
| `ClamProtocolException` | clamd 回傳非預期或無法解析的回應 |

## 授權條款

MIT
