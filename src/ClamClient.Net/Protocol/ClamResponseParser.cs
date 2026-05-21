using ClamClient.Net.Results;

namespace ClamClient.Net.Protocol;

/// <summary>
/// Parses and interprets responses from a ClamAV server.
/// </summary>
internal static class ClamResponseParser
{
    /// <summary>
    /// Determines if a raw response is a PONG reply to a PING command.
    /// </summary>
    /// <param name="raw">The raw response string from the server.</param>
    /// <returns>True if the response is a PONG reply; otherwise, false.</returns>
    internal static bool IsPong(string raw) =>
        raw.Trim('\0').Equals("PONG", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses a SCAN, MULTISCAN, or INSTREAM response.
    /// Handles both single-line (SCAN/INSTREAM) and multi-line (MULTISCAN) responses.
    /// </summary>
    internal static ScanResult ParseScanResponse(string raw)
    {
        var trimmed = raw.Trim('\0', '\n', '\r');
#if NET5_0_OR_GREATER
        var lines = trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries);
#else
        var lines = trimmed.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
#endif

        if (lines.Length == 0)
        {
            return new(ScanStatus.Unknown, raw);
        }

        var threats = new List<DetectedThreat>();
        var hasError = false;

        foreach (var line in lines)
        {
            var l = line.TrimEnd('\r', '\0');

            if (l.EndsWith(" OK", StringComparison.Ordinal))
                continue;

            if (l.EndsWith(" FOUND", StringComparison.Ordinal))
            {
                // Format: "filename: ThreatName FOUND"
                var colonIdx = l.IndexOf(": ", StringComparison.Ordinal);
                if (colonIdx >= 0)
                {
                    var fileName = l.Substring(0, colonIdx);

                    // "ThreatName FOUND"
                    var rest = l.Substring(colonIdx + 2);

                    const string foundSuffix = " FOUND";
                    var nameLength = rest.Length - foundSuffix.Length;
                    if (nameLength <= 0)
                    {
                        hasError = true;
                        continue;
                    }
                    var threatName = rest.Substring(0, nameLength);
                    threats.Add(new(fileName, threatName));
                }
                else
                {
                    threats.Add(new(string.Empty, l));
                }
                continue;
            }

            if (l.EndsWith(" ERROR", StringComparison.Ordinal))
            {
                hasError = true;
                continue;
            }

            // Unrecognised line — treat as error
            hasError = true;
        }

        if (threats.Count > 0)
        {
            return new(ScanStatus.ThreatFound, raw, threats);
        }

        if (hasError)
        {
            return new(ScanStatus.Error, raw);
        }

        return new(ScanStatus.Clean, raw);
    }
}
