namespace ClamClient.Net.Results;

/// <summary>
/// The outcome of a ClamAV scan.
/// </summary>
public enum ScanStatus
{
    /// <summary>
    /// No threats were detected.
    /// </summary>
    Clean,

    /// <summary>
    /// One or more threats were detected.
    /// </summary>
    ThreatFound,

    /// <summary>
    /// clamd reported an error during scanning.
    /// </summary>
    Error,

    /// <summary>
    /// The response could not be parsed.
    /// </summary>
    Unknown
}