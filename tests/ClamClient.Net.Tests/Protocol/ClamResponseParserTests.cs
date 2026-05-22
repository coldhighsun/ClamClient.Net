using ClamClient.Net.Protocol;
using ClamClient.Net.Results;

namespace ClamClient.Net.Tests.Protocol;

public sealed class ClamResponseParserTests
{
    [Theory]
    [InlineData("")]
    [InlineData("OK")]
    [InlineData("PING")]
    [InlineData("stream: PONG FOUND")]
    public void IsPong_ReturnsFalseForNonPong(string input)
    {
        Assert.False(ClamResponseParser.IsPong(input));
    }

    [Theory]
    [InlineData("PONG")]
    [InlineData("PONG\0")]
    [InlineData("pong")]
    public void IsPong_ReturnsTrueForValidPong(string input)
    {
        Assert.True(ClamResponseParser.IsPong(input));
    }

    [Fact]
    public void ParseScanResponse_Clean_ReturnsCleanStatus()
    {
        var result = ClamResponseParser.ParseScanResponse("stream: OK");

        Assert.Equal(ScanStatus.Clean, result.Status);
        Assert.Empty(result.Threats);
    }

    [Fact]
    public void ParseScanResponse_EmptyResponse_ReturnsUnknown()
    {
        var result = ClamResponseParser.ParseScanResponse("");

        Assert.Equal(ScanStatus.Unknown, result.Status);
    }

    [Fact]
    public void ParseScanResponse_Error_ReturnsErrorStatus()
    {
        var result = ClamResponseParser.ParseScanResponse("/path/to/file: lstat() failed. ERROR");

        Assert.Equal(ScanStatus.Error, result.Status);
        Assert.Empty(result.Threats);
    }

    [Fact]
    public void ParseScanResponse_FileScan_ExtractsCorrectFileName()
    {
        var result = ClamResponseParser.ParseScanResponse("/var/scan/test.exe: Win.Virus FOUND");

        Assert.Equal(ScanStatus.ThreatFound, result.Status);
        Assert.Equal("/var/scan/test.exe", result.Threats[0].FileName);
        Assert.Equal("Win.Virus", result.Threats[0].ThreatName);
    }

    [Fact]
    public void ParseScanResponse_MalformedFoundWithNoThreatName_ReturnsError()
    {
        // A malformed response where there is no threat name before FOUND
        // e.g. "file.exe: FOUND" — rest = "FOUND", which is shorter than " FOUND"
        var result = ClamResponseParser.ParseScanResponse("file.exe: FOUND");

        Assert.Equal(ScanStatus.Error, result.Status);
        Assert.Empty(result.Threats);
    }

    [Fact]
    public void ParseScanResponse_MultiLine_AggregatesThreats()
    {
        var response = "/tmp/a.txt: OK\n/tmp/b.exe: Trojan.Generic FOUND\n/tmp/c.txt: OK";
        var result = ClamResponseParser.ParseScanResponse(response);

        Assert.Equal(ScanStatus.ThreatFound, result.Status);
        Assert.Single(result.Threats);
        Assert.Equal("/tmp/b.exe", result.Threats[0].FileName);
        Assert.Equal("Trojan.Generic", result.Threats[0].ThreatName);
    }

    [Fact]
    public void ParseScanResponse_MultiLine_MultipleThreats()
    {
        var response = "/tmp/a.exe: Eicar FOUND\n/tmp/b.exe: Trojan FOUND";
        var result = ClamResponseParser.ParseScanResponse(response);

        Assert.Equal(ScanStatus.ThreatFound, result.Status);
        Assert.Equal(2, result.Threats.Count);
    }

    [Fact]
    public void ParseScanResponse_MultiLine_ThreatBeatsError()
    {
        var response = "/tmp/a.txt: some error ERROR\n/tmp/b.exe: Eicar FOUND";
        var result = ClamResponseParser.ParseScanResponse(response);

        Assert.Equal(ScanStatus.ThreatFound, result.Status);
        Assert.Single(result.Threats);
    }

    [Fact]
    public void ParseScanResponse_NullTerminatedResponse_HandledCorrectly()
    {
        var result = ClamResponseParser.ParseScanResponse("stream: OK\0");

        Assert.Equal(ScanStatus.Clean, result.Status);
    }

    [Fact]
    public void ParseScanResponse_ThreatFound_ReturnsThreatFoundStatus()
    {
        var result = ClamResponseParser.ParseScanResponse("stream: Eicar-Test-Signature FOUND");

        Assert.Equal(ScanStatus.ThreatFound, result.Status);
        Assert.Single(result.Threats);
        Assert.Equal("stream", result.Threats[0].FileName);
        Assert.Equal("Eicar-Test-Signature", result.Threats[0].ThreatName);
    }
}