using TrispotQR.Core.Payloads;

namespace TrispotQR.Tests;

public class UrlCheckTests
{
    [Theory]
    [InlineData("example.org", "https://example.org")]
    [InlineData("www.example.org/give", "https://www.example.org/give")]
    [InlineData("https://www.example.org", "https://www.example.org")]
    [InlineData("http://example.com/a?b=1&c=2", "http://example.com/a?b=1&c=2")]
    [InlineData("example.com:8080/status", "https://example.com:8080/status")]
    [InlineData("localhost:5000", "https://localhost:5000")]
    [InlineData("192.168.1.10/admin", "https://192.168.1.10/admin")]
    public void CheckUrl_ValidAddresses_AreAcceptedAndGetASchemeWhenMissing(string input, string expected)
    {
        var result = PayloadBuilder.CheckUrl(input);

        Assert.True(result.IsValid, $"expected '{input}' to be valid but got: {result.Message}");
        Assert.Equal(expected, result.Payload);
    }

    [Fact]
    public void CheckUrl_BareDomain_ExplainsThatHttpsWasAdded()
    {
        var result = PayloadBuilder.CheckUrl("example.org");

        Assert.True(result.IsValid);
        Assert.Contains("https://example.org", result.Message!);
    }

    [Fact]
    public void CheckUrl_AddressAlreadyComplete_SaysNothing()
    {
        var result = PayloadBuilder.CheckUrl("https://example.org");

        Assert.True(result.IsValid);
        Assert.Null(result.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CheckUrl_Empty_AsksForAnAddress(string input)
    {
        var result = PayloadBuilder.CheckUrl(input);

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void CheckUrl_PlainSentence_IsRejectedBecauseItHasSpaces()
    {
        var result = PayloadBuilder.CheckUrl("open day on friday");

        Assert.False(result.IsValid);
        Assert.Contains("space", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckUrl_SingleWordWithNoDot_IsRejected()
    {
        var result = PayloadBuilder.CheckUrl("exampleorg");

        Assert.False(result.IsValid);
        Assert.Contains("exampleorg", result.Message!);
    }

    [Fact]
    public void CheckUrl_MistypedScheme_NamesTheProblem()
    {
        var result = PayloadBuilder.CheckUrl("htp://example.com");

        Assert.False(result.IsValid);
        Assert.Contains("https://", result.Message!);
    }

    [Fact]
    public void CheckUrl_NonWebScheme_IsRejected()
    {
        var result = PayloadBuilder.CheckUrl("ftp://files.example.com");

        Assert.False(result.IsValid);
        Assert.Contains("ftp", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckUrl_TrailingDotOnlyTld_IsRejected()
    {
        var result = PayloadBuilder.CheckUrl("example.");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CheckUrl_InvalidInput_StillReturnsAUsablePayloadSoThePreviewKeepsWorking()
    {
        var result = PayloadBuilder.CheckUrl("exampleorg");

        Assert.False(result.IsValid);
        Assert.Equal("https://exampleorg", result.Payload);
    }
}
