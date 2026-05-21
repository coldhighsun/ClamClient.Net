using ClamClient.Net.Protocol;
using ClamClient.Net.Results;

namespace ClamClient.Net.Tests.Results;

public sealed class ScanResultTests
{
    [Fact]
    public void ScanResult_Clean_HasEmptyThreats()
    {
        var result = ClamResponseParser.ParseScanResponse("stream: OK");

        Assert.Equal(ScanStatus.Clean, result.Status);
        Assert.Empty(result.Threats);
    }

    [Fact]
    public void ScanResult_ThreatFound_HasThreats()
    {
        var result = ClamResponseParser.ParseScanResponse("stream: Eicar FOUND");

        Assert.Equal(ScanStatus.ThreatFound, result.Status);
        Assert.Single(result.Threats);
        Assert.Equal("Eicar", result.Threats[0].ThreatName);
    }

    [Fact]
    public void DetectedThreat_IsValueEqual()
    {
        var a = new DetectedThreat("file.exe", "Trojan.Generic");
        var b = new DetectedThreat("file.exe", "Trojan.Generic");

        Assert.Equal(a, b);
    }

    [Fact]
    public void ScanResult_ToString_IncludesThreatName()
    {
        var result = ClamResponseParser.ParseScanResponse("stream: Eicar-Test-Signature FOUND");

        var str = result.ToString();
        Assert.Contains("Eicar-Test-Signature", str);
        Assert.Contains("ThreatFound", str);
    }
}
