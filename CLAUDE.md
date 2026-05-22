# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
# Build the solution
dotnet build

# Run all tests
dotnet test

# Run a single test class
dotnet test --filter "FullyQualifiedName~ClamClientIntegrationTests"

# Run a single test method
dotnet test --filter "FullyQualifiedName~ScanStreamAsync_WhenInfected_ReturnsThreatFound"
```

## Architecture

ClamClient.Net is a single-library solution (`src/ClamClient.Net`) with a paired test project (`tests/ClamClient.Net.Tests`).

**Request flow:**  
Caller → `ClamAVClient` → `ClamConnectionPool` (rent idle connection or open new one) → `ClamConnection` → writes null-terminated command (e.g., `"zPING\0"`) → reads null-terminated response → `ClamResponseParser` → `ScanResult` → connection returned to pool.

Connections use IDSESSION mode (`zIDSESSION\0` sent once on open, `zEND\0` sent on close), which allows multiple sequential commands per TCP connection without the server closing the socket.

For stream scans: `ClamAVClient.ScanStreamAsync` uses `InStreamWriter` to frame the payload as big-endian 4-byte length-prefixed chunks, terminated by a 4-byte zero block, before parsing the null-terminated response.

**Key types:**
- `IClamClient` — public contract; all async methods return `ScanResult` or primitive values
- `ClamAVClient` — implementation; owns a `ClamConnectionPool` and delegates all sends through it; two `ExecuteCommandAsync` overloads — one for pre-encoded `byte[]` commands, one for dynamic string commands (SCAN, MULTISCAN)
- `ClamClientOptions` — timeout, chunk size, max stream size, pool settings (`Timeout`, `ChunkSize`, `MaxStreamSize`, `MaxConnections`, `IdleConnectionTimeout`); defaults: endpoint `localhost:3310`, chunk size 128 KB, max stream size 25 MB, timeout 10 s, max connections 10, idle timeout 30 s; `MaxConnections = 0` disables the semaphore cap (unlimited connections)
- `ClamEndpoint` — discriminated union of TCP (`Host`/`Port`) or Unix domain socket (`SocketPath`)
- `ScanResult` — contains `ScanStatus` enum and `IReadOnlyList<DetectedThreat>`; status priority is `ThreatFound > Error > Clean > Unknown`
- `DetectedThreat` — `sealed record` with positional parameters `(FileName, ThreatName)`; value equality is intentional and relied on by tests
- `ClamResponseParser` — parses single-line (`OK`/`FOUND`/`ERROR`) and multi-line (`MULTISCAN`) responses; internal, exposed via `InternalsVisibleTo`
- `ClamConnectionPool` (`Pool/`) — manages idle `ClamConnection`s; `SemaphoreSlim` caps concurrent connections; `ConcurrentQueue<ClamConnection>` holds idle ones; semaphore slot stays occupied while a connection is idle and is only released on eviction or unhealthy return; internal
- `ClamConnection` (`Pool/`) — wraps a stream in IDSESSION mode; exposes `ExecuteAsync` (both `byte[]` and `string` overloads); strips numeric IDSESSION response prefixes (`N: `) via `StripIdSessionPrefixes`; reads into a 4 KB `_readBuffer` with `MemoryStream` overflow for long responses (e.g. STATS); tracks `LastUsedAt` for idle eviction; internal

**Stale-connection retry:** All `ClamAVClient` methods automatically retry once on `ClamConnectionException` (the failed connection is evicted, a fresh one is opened). `ScanStreamAsync` only retries when the stream is seekable (position is reset to the original value before retrying).

**Exceptions thrown by `ClamAVClient`:**
- `ClamConnectionException` — connection to clamd failed or was lost
- `ClamProtocolException` — unexpected/malformed response from clamd
- `ClamStreamSizeExceededException` — input stream exceeds `ClamClientOptions.MaxStreamSize`

**DI registration:**
```csharp
services.AddClamClient(options => { options.Endpoint = ClamEndpoint.Tcp("localhost", 3310); });
```

`IClamClient` is registered as **Singleton** (shared connection pool); `ClamClientOptions` as **Singleton**. `ClamAVClient` implements `IAsyncDisposable` — dispose it to drain the pool and send `zEND\0` on all idle connections.

`ClamEndpoint` is constructed via static factory methods (`ClamEndpoint.Tcp(...)` / `ClamEndpoint.UnixSocket(...)`), not `new`. `UnixSocket` throws `PlatformNotSupportedException` on Windows.

## Code Conventions

- All compiler warnings are errors (`TreatWarningsAsErrors=true`).
- Nullable reference types and implicit usings are enabled project-wide.
- All members require XML doc comments — including `private` and `internal`, not just public ones. Always use the three-line `/// <summary>` format; never the single-line collapsed form. Add `<param>` and `<returns>` tags for non-obvious parameters/return values. (`GenerateDocumentationFile=true` enforces this for public members at build time.)
- All async code uses `ConfigureAwait(false)`.
- Package versions are centrally managed in `Directory.Packages.props` — add new packages there, not directly in `.csproj`.
- The library targets `netstandard2.0` and `net8.0`. Polyfills for `Memory<T>`, `BinaryPrimitives`, and `IAsyncDisposable` are conditionally included for `netstandard2.0` via `System.Memory` and `Microsoft.Bcl.AsyncInterfaces`.
- Versioning is handled by MinVer — git tags of the form `v1.2.3` drive the NuGet package version. Do not manually edit version properties.

## Testing Approach

Tests do **not** require a live clamd process. `FakeClamdServer` (`tests/Fakes/`) is an in-process TCP listener that accepts scripted byte sequences and responses; integration tests use it exclusively. Unit tests cover the parser and stream-writer directly via `InternalsVisibleTo`.

`FakeClamdServer` handles **one connection per instance** — create a new instance per test case. It understands IDSESSION (skips silently), END (closes), and normal commands. `ReceivedBytes` captures the wire bytes of the **first real command** (including any INSTREAM payload). Pass multiple scripted responses as `params string[]` to `Start`. `ConnectionCount` exposes the number of accepted TCP connections. Responses are automatically prefixed with IDSESSION sequence numbers (`N: `) to mirror real clamd behavior; `ClamConnection.StripIdSessionPrefixes` strips them on the client side.

When adding new commands: add a scripted response to `FakeClamdServer`, write an integration test in `ClamClientIntegrationTests`, and add parser coverage in `ClamResponseParserTests`.

Always use `await using var client = BuildClient(...)` in integration tests so the pool sends `zEND\0` before the server is disposed.

`ScanResult` has an `internal` constructor; only `ClamResponseParser` produces instances. Tests that need a `ScanResult` must go through the parser or the fake server path. `ClamResponseParser`, `InStreamWriter`, and `ScanResult`'s internal constructor are all accessible to tests via `InternalsVisibleTo("ClamClient.Net.Tests")`.
