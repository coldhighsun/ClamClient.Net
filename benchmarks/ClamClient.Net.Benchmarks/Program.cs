using BenchmarkDotNet.Running;

// ============================================================
// ClamClient.Net — connection pool benchmarks (BenchmarkDotNet)
//
// Prerequisites: a running clamd daemon (default localhost:3310).
//   Docker:  docker run -p 3310:3310 clamav/clamav:latest
// Override the endpoint with the CLAMD_HOST / CLAMD_PORT environment variables.
//
// Run (Release is required by BenchmarkDotNet):
//   dotnet run -c Release --project benchmarks/ClamClient.Net.Benchmarks -- --filter "*"
// ============================================================

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

/// <summary>
/// Entry point holder so the assembly can be referenced from top-level statements.
/// </summary>
internal sealed partial class Program;
