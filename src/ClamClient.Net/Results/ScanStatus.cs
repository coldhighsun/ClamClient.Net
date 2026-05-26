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
    /// The input stream exceeded the configured <see cref="Configuration.ClamClientOptions.MaxStreamSize"/> limit.
    /// No data was sent to clamd.
    /// </summary>
    StreamTooLarge
}
