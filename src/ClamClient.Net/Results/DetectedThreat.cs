namespace ClamClient.Net.Results;

/// <summary>
/// A single threat detected by ClamAV.
/// </summary>
/// <param name="FileName">The file or stream identifier clamd reported.</param>
/// <param name="ThreatName">The threat signature name.</param>
public sealed record DetectedThreat(string FileName, string ThreatName);