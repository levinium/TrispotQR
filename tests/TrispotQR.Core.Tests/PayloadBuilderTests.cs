using TrispotQR.Core.Payloads;

namespace TrispotQR.Tests;

public class PayloadBuilderTests
{
    [Theory]
    [InlineData("example.org", "https://example.org")]
    [InlineData("www.example.org", "https://www.example.org")]
    [InlineData("  example.org  ", "https://example.org")]
    [InlineData("http://example.com", "http://example.com")]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("HTTPS://EXAMPLE.COM", "HTTPS://EXAMPLE.COM")]
    [InlineData("mailto:a@b.com", "mailto:a@b.com")]
    public void Url_AddsHttpsOnlyWhenNoSchemeIsPresent(string input, string expected)
    {
        Assert.Equal(expected, PayloadBuilder.Url(input));
    }

    [Fact]
    public void Wifi_BuildsTheStandardPayload()
    {
        var payload = PayloadBuilder.Wifi("GuestNetwork", "hunter2", WifiSecurity.Wpa, hidden: false);

        Assert.Equal("WIFI:T:WPA;S:GuestNetwork;P:hunter2;;", payload);
    }

    [Fact]
    public void Wifi_EscapesTheReservedCharacters()
    {
        // Backslash, semicolon, comma, colon and double quote all have to be escaped
        // or the phone parses the payload into the wrong fields.
        var payload = PayloadBuilder.Wifi(@"Net;work", @"p\a,s:s""1", WifiSecurity.Wpa, hidden: false);

        Assert.Equal(@"WIFI:T:WPA;S:Net\;work;P:p\\a\,s\:s\""1;;", payload);
    }

    [Fact]
    public void Wifi_OpenNetwork_UsesNopassAndOmitsThePassword()
    {
        var payload = PayloadBuilder.Wifi("OpenNet", "", WifiSecurity.None, hidden: false);

        Assert.Equal("WIFI:T:nopass;S:OpenNet;;", payload);
    }

    [Fact]
    public void Wifi_HiddenNetwork_SetsTheHiddenFlag()
    {
        var payload = PayloadBuilder.Wifi("Hidden", "pw", WifiSecurity.Wpa, hidden: true);

        Assert.Equal("WIFI:T:WPA;S:Hidden;P:pw;H:true;;", payload);
    }

    [Fact]
    public void Email_WithoutSubjectOrBody_IsAPlainMailto()
    {
        Assert.Equal("mailto:jordan.reed@example.org",
            PayloadBuilder.Email("jordan.reed@example.org", null, null));
    }

    [Fact]
    public void Email_WithSubjectAndBody_PercentEncodesTheQuery()
    {
        var payload = PayloadBuilder.Email("a@b.org", "Hi there", "Line one & two");

        Assert.Equal("mailto:a@b.org?subject=Hi%20there&body=Line%20one%20%26%20two", payload);
    }

    [Theory]
    [InlineData("212-555-1234", "tel:+12125551234")]
    [InlineData("(212) 555 1234", "tel:+12125551234")]
    [InlineData("+44 20 7946 0958", "tel:+442079460958")]
    [InlineData("12125551234", "tel:+12125551234")]
    public void Phone_StripsFormattingAndKeepsALeadingPlus(string input, string expected)
    {
        Assert.Equal(expected, PayloadBuilder.Phone(input));
    }

    [Fact]
    public void Sms_UsesSmstoWithTheMessage()
    {
        Assert.Equal("SMSTO:+12125551234:See you Friday", PayloadBuilder.Sms("212-555-1234", "See you Friday"));
    }

    [Fact]
    public void Sms_WithoutAMessage_OmitsTheTrailingColon()
    {
        Assert.Equal("SMSTO:+12125551234", PayloadBuilder.Sms("212-555-1234", null));
    }

    [Fact]
    public void VCard_ProducesAWellFormedVersion3Card()
    {
        var card = new ContactCard
        {
            FirstName = "Mark",
            LastName = "Levy",
            Organization = "Northgate Studios",
            Title = "IT Director",
            Phone = "212-555-1234",
            Email = "jordan.reed@example.org",
            Website = "example.org",
        };

        var payload = PayloadBuilder.VCard(card);
        var lines = payload.Split("\r\n");

        Assert.Equal("BEGIN:VCARD", lines[0]);
        Assert.Equal("VERSION:3.0", lines[1]);
        Assert.Equal("END:VCARD", lines[^1]);
        Assert.Contains("N:Levy;Mark;;;", lines);
        Assert.Contains("FN:Mark Levy", lines);
        Assert.Contains("ORG:Northgate Studios", lines);
        Assert.Contains("TITLE:IT Director", lines);
        Assert.Contains("TEL;TYPE=CELL:+12125551234", lines);
        Assert.Contains("EMAIL:jordan.reed@example.org", lines);
        Assert.Contains("URL:https://example.org", lines);
    }

    [Fact]
    public void VCard_EscapesReservedCharactersInTextValues()
    {
        var card = new ContactCard
        {
            FirstName = "A,B",
            LastName = "C;D",
            Organization = @"E\F",
        };

        var lines = PayloadBuilder.VCard(card).Split("\r\n");

        Assert.Contains(@"N:C\;D;A\,B;;;", lines);
        Assert.Contains(@"ORG:E\\F", lines);
    }

    [Fact]
    public void VCard_OmitsEmptyFields()
    {
        var card = new ContactCard { FirstName = "Solo" };

        var payload = PayloadBuilder.VCard(card);

        Assert.DoesNotContain("ORG:", payload);
        Assert.DoesNotContain("TEL", payload);
        Assert.DoesNotContain("EMAIL:", payload);
        Assert.Contains("FN:Solo", payload);
    }
}
