using ClamClient.Net;
using ClamClient.Net.Abstractions;
using ClamClient.Net.Configuration;
using ClamClient.Net.Exceptions;
using ClamClient.Net.Extensions;
using ClamClient.Net.Results;
using Microsoft.Extensions.DependencyInjection;

// ============================================================
// ClamClient.Net — Quick Start Sample
//
// Prerequisites: a running clamd daemon on localhost:3310.
// Docker:  docker run -p 3310:3310 clamav/clamav:latest
// ============================================================

Console.WriteLine("=== ClamClient.Net Quick Start ===");
Console.WriteLine();

// ----------------------------------------------------------
// Example 1: Direct instantiation (no DI)
// ----------------------------------------------------------
Console.WriteLine("--- Example 1: Direct instantiation ---");

var options = new ClamClientOptions
{
    Endpoint = ClamEndpoint.Tcp("localhost", 3310),
    Timeout = TimeSpan.FromSeconds(15),
    MaxStreamSize = 50L * 1024 * 1024,   // 50 MB
};

IClamClient client = new ClamAVClient(options);

// Ping
var alive = await client.PingAsync();
Console.WriteLine($"Ping: {(alive ? "PONG — clamd is up" : "no response")}");

// Version
var version = await client.GetVersionAsync();
Console.WriteLine($"Version: {version}");

// Stats
var stats = await client.GetStatsAsync();
Console.WriteLine($"Stats (first line): {stats.Split('\n')[0]}");

Console.WriteLine();

// ----------------------------------------------------------
// Example 2: Scan a clean stream
// ----------------------------------------------------------
Console.WriteLine("--- Example 2: Scan a clean byte stream ---");

var cleanBytes = "Hello, world!"u8.ToArray();
using var cleanStream = new MemoryStream(cleanBytes);

var cleanResult = await client.ScanStreamAsync(cleanStream);
PrintResult(cleanResult);

Console.WriteLine();

// ----------------------------------------------------------
// Example 3: Scan the EICAR test string (detected as a virus)
// ----------------------------------------------------------
Console.WriteLine("--- Example 3: Scan the EICAR test string ---");

// The EICAR standard test file — safe, but always detected by ClamAV.
const string eicar =
    @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

using var eicarStream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(eicar));

var eicarResult = await client.ScanStreamAsync(eicarStream);
PrintResult(eicarResult);

if (eicarResult.Status == ScanStatus.ThreatFound)
{
    foreach (var threat in eicarResult.Threats)
        Console.WriteLine($"  Threat: {threat.ThreatName} (file: {threat.FileName})");
}

Console.WriteLine();

// ----------------------------------------------------------
// Example 4: Scan a file on the clamd host
// ----------------------------------------------------------
Console.WriteLine("--- Example 4: Scan a file path on the clamd host ---");

// The path must be accessible from clamd's perspective (same host / same container mount).
const string remoteFilePath = "/tmp/test.txt";
try
{
    var fileResult = await client.ScanFileAsync(remoteFilePath);
    PrintResult(fileResult);
}
catch (ClamConnectionException ex)
{
    Console.WriteLine($"  Connection error: {ex.Message}");
}

Console.WriteLine();

// ----------------------------------------------------------
// Example 5: Reload virus database
// ----------------------------------------------------------
Console.WriteLine("--- Example 5: Reload virus database ---");
await client.ReloadAsync();
Console.WriteLine("  Reload command sent.");

Console.WriteLine();

// ----------------------------------------------------------
// Example 6: Dependency Injection (ASP.NET Core / Generic Host)
// ----------------------------------------------------------
Console.WriteLine("--- Example 6: Dependency Injection ---");

var services = new ServiceCollection();

services.AddClamClient(opt =>
{
    opt.Endpoint = ClamEndpoint.Tcp("localhost", 3310);
    opt.Timeout = TimeSpan.FromSeconds(10);
});

// Optionally register a scanning service that depends on IClamClient.
services.AddTransient<FileUploadScanner>();

var provider = services.BuildServiceProvider();
var scanner = provider.GetRequiredService<FileUploadScanner>();

var uploadedFile = "safe content"u8.ToArray();
var safe = await scanner.IsSafeAsync(uploadedFile);
Console.WriteLine($"  Uploaded file is {(safe ? "safe" : "INFECTED")}.");

Console.WriteLine();
Console.WriteLine("Done.");

// ----------------------------------------------------------
// Helper
// ----------------------------------------------------------
static void PrintResult(ScanResult result)
{
    var icon = result.Status switch
    {
        ScanStatus.Clean => "OK",
        ScanStatus.ThreatFound => "!!",
        ScanStatus.Error => "ERR",
        _ => "?",
    };
    Console.WriteLine($"  [{icon}] {result}");
}

// ----------------------------------------------------------
// Example service that uses IClamClient via DI
// ----------------------------------------------------------
internal sealed class FileUploadScanner(IClamClient clamClient)
{
    public async Task<bool> IsSafeAsync(byte[] fileBytes, CancellationToken ct = default)
    {
        using var stream = new MemoryStream(fileBytes);
        var result = await clamClient.ScanStreamAsync(stream, ct);
        return result.Status == ScanStatus.Clean;
    }
}