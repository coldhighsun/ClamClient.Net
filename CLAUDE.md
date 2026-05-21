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
Caller → `ClamAVClient` → opens TCP/Unix socket → writes null-terminated command (e.g., `"zPING\0"`) → reads response → `ClamResponseParser` → `ScanResult`.

For stream scans: `ClamAVClient.ScanStreamAsync` uses `InStreamWriter` to frame the payload as big-endian 4-byte length-prefixed chunks, terminated by a 4-byte zero block, before parsing the single-line response.

**Key types:**
- `IClamClient` — public contract; all async methods return `ScanResult` or primitive values
- `ClamAVClient` — implementation; creates one `TcpClient`/`Socket` per call (stateless)
- `ClamClientOptions` — timeout, chunk size, max stream size (`Timeout`, `ChunkSize`, `MaxStreamSize`)
- `ClamEndpoint` — discriminated union of TCP (`Host`/`Port`) or Unix domain socket (`SocketPath`)
- `ScanResult` — contains `ScanStatus` enum and `IReadOnlyList<DetectedThreat>`; status priority is `ThreatFound > Error > Clean > Unknown`
- `ClamResponseParser` — parses single-line (`OK`/`FOUND`/`ERROR`) and multi-line (`MULTISCAN`) responses; internal, exposed via `InternalsVisibleTo`

**DI registration:**
```csharp
services.AddClamClient(options => { options.Endpoint = ClamEndpoint.Tcp("localhost", 3310); });
```

`ClamEndpoint` is constructed via static factory methods (`ClamEndpoint.Tcp(...)` / `ClamEndpoint.UnixSocket(...)`), not `new`. `UnixSocket` throws `PlatformNotSupportedException` on Windows.

## Code Conventions

- All compiler warnings are errors (`TreatWarningsAsErrors=true`).
- Nullable reference types and implicit usings are enabled project-wide.
- All public members require XML doc comments (`GenerateDocumentationFile=true`).
- All async code uses `ConfigureAwait(false)`.
- Package versions are centrally managed in `Directory.Packages.props` — add new packages there, not directly in `.csproj`.

## Testing Approach

Tests do **not** require a live clamd process. `FakeClamdServer` (`tests/Fakes/`) is an in-process TCP listener that accepts scripted byte sequences and responses; integration tests use it exclusively. Unit tests cover the parser and stream-writer directly via `InternalsVisibleTo`.

`FakeClamdServer` handles **one connection per instance** — create a new instance per test case. It auto-reads INSTREAM chunk frames in full so `ReceivedBytes` captures the complete wire payload.

When adding new commands: add a scripted response to `FakeClamdServer`, write an integration test in `ClamClientIntegrationTests`, and add parser coverage in `ClamResponseParserTests`.

`ScanResult` has an `internal` constructor; only `ClamResponseParser` produces instances. Tests that need a `ScanResult` must go through the parser or the fake server path.
