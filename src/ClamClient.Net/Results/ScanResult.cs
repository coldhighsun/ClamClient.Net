namespace ClamClient.Net.Results;

/// <summary>
/// The result of a ClamAV scan operation.
/// </summary>
public sealed class ScanResult
{
    /// <summary>
    /// Initializes a new instance of the ScanResult class with the specified status, raw response, and detected threats.
    /// </summary>
    /// <param name="status">The overall scan outcome.</param>
    /// <param name="rawResponse">The raw response string received from clamd.</param>
    /// <param name="threats">The list of detected threats.</param>
    internal ScanResult(ScanStatus status, string rawResponse, IReadOnlyList<DetectedThreat>? threats = null)
    {
        Status = status;
        RawResponse = rawResponse;
        Threats = threats ?? Array.Empty<DetectedThreat>();
    }

    /// <summary>
    /// The raw response string received from clamd.
    /// </summary>
    public string RawResponse
    {
        get;
    }

    /// <summary>
    /// The overall scan outcome.
    /// </summary>
    public ScanStatus Status
    {
        get;
    }

    /// <summary>
    /// Threats detected. Non-empty only when <see cref="Status"/> is <see cref="ScanStatus.ThreatFound"/>.
    /// </summary>
    public IReadOnlyList<DetectedThreat> Threats
    {
        get;
    }

    /// <summary>
    /// Returns the scan status and detected threat names, or the raw response when no threats were found.
    /// </summary>
    public override string ToString() =>
        $"{Status}: {(Threats.Count > 0 ? string.Join(", ", Threats.Select(t => t.ThreatName)) : RawResponse)}";
}