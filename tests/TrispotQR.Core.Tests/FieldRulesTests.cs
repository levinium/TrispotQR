using TrispotQR.Core.Payloads;

namespace TrispotQR.Tests;

/// <summary>
/// The per-field rules behind the red highlighting.
///
/// These are deliberately in Core and pure, so the question "is this a usable phone number"
/// is answered and tested in one place rather than inside a WPF control where it can only be
/// checked by clicking.
/// </summary>
public class FieldRulesTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Required_RejectsBlank(string? value) =>
        Assert.NotNull(FieldRules.Required(value, "a network name"));

    [Fact]
    public void Required_NamesWhatIsMissing() =>
        Assert.Equal("Enter a network name.", FieldRules.Required(" ", "a network name"));

    [Fact]
    public void Required_AcceptsAnythingElse() => Assert.Null(FieldRules.Required("x", "a name"));

    [Theory]
    [InlineData("someone@example.org")]
    [InlineData("first.last+tag@mail.example.co.uk")]
    [InlineData("  spaced@example.org  ")]
    public void Email_AcceptsRealAddresses(string value) => Assert.Null(FieldRules.Email(value));

    [Theory]
    [InlineData("someone")]
    [InlineData("someone@")]
    [InlineData("@example.org")]
    [InlineData("someone@example")]
    [InlineData("some one@example.org")]
    [InlineData("two@@example.org")]
    [InlineData("someone@example.o")]
    public void Email_RejectsMalformedAddresses(string value) => Assert.NotNull(FieldRules.Email(value));

    [Fact]
    public void Email_ExplainsAMissingAtSign() =>
        Assert.Contains("@", FieldRules.Email("someone.example.org"));

    /// <summary>
    /// An empty value is the Required rule's business, not the format rule's, or a blank
    /// field would report two problems at once.
    /// </summary>
    [Fact]
    public void Email_IgnoresBlank() => Assert.Null(FieldRules.Email(""));

    [Theory]
    [InlineData("212 555 0134")]
    [InlineData("+44 20 7946 0958")]
    [InlineData("(212) 555-0134")]
    [InlineData("212.555.0134")]
    public void Phone_AcceptsCommonFormats(string value) => Assert.Null(FieldRules.Phone(value));

    [Theory]
    [InlineData("12345")]
    [InlineData("555-01")]
    public void Phone_RejectsTooFewDigits(string value) =>
        Assert.Contains("short", FieldRules.Phone(value));

    /// <summary>
    /// Seven digits is a local number with no area code, which is a real thing to put on a
    /// poster for a local audience, so the floor sits just below it rather than above.
    /// </summary>
    [Fact]
    public void Phone_AcceptsASevenDigitLocalNumber() => Assert.Null(FieldRules.Phone("555 0134"));

    [Fact]
    public void Phone_RejectsLetters() => Assert.NotNull(FieldRules.Phone("212 555 CALL"));

    [Fact]
    public void Phone_RejectsAPlusThatIsNotLeading() =>
        Assert.Contains("plus", FieldRules.Phone("212 555+0134"));

    [Fact]
    public void Phone_IgnoresBlank() => Assert.Null(FieldRules.Phone(" "));

    [Theory]
    [InlineData("example.org")]
    [InlineData("https://example.org/tickets")]
    [InlineData("http://localhost:8080")]
    public void Website_AcceptsRealAddresses(string value) => Assert.Null(FieldRules.Website(value));

    [Theory]
    [InlineData("not a website")]
    [InlineData("example")]
    [InlineData("mailto:someone@example.org")]
    public void Website_RejectsTheRest(string value) => Assert.NotNull(FieldRules.Website(value));

    [Fact]
    public void Website_IgnoresBlank() => Assert.Null(FieldRules.Website(""));

    [Fact]
    public void Ssid_AcceptsANormalName() => Assert.Null(FieldRules.Ssid("Guest Wi-Fi"));

    /// <summary>The 32 is a byte limit in the standard, not a character one.</summary>
    [Fact]
    public void Ssid_RejectsOverThirtyTwoBytes()
    {
        Assert.Null(FieldRules.Ssid(new string('a', 32)));
        Assert.NotNull(FieldRules.Ssid(new string('a', 33)));
        Assert.NotNull(FieldRules.Ssid(new string('é', 17)));
    }

    [Fact]
    public void WifiPassword_IsNotWantedOnAnOpenNetwork() =>
        Assert.Null(FieldRules.WifiPassword("", WifiSecurity.None));

    [Fact]
    public void WifiPassword_IsRequiredOnASecuredNetwork()
    {
        Assert.NotNull(FieldRules.WifiPassword("", WifiSecurity.Wpa));
        Assert.NotNull(FieldRules.WifiPassword("", WifiSecurity.Wep));
    }

    [Theory]
    [InlineData("short7")]
    [InlineData("1234567")]
    public void WifiPassword_RejectsAShortWpaKey(string value) =>
        Assert.NotNull(FieldRules.WifiPassword(value, WifiSecurity.Wpa));

    [Fact]
    public void WifiPassword_AcceptsAValidWpaKey() =>
        Assert.Null(FieldRules.WifiPassword("longenough", WifiSecurity.Wpa));

    [Fact]
    public void WifiPassword_RejectsAnOverlongWpaKey() =>
        Assert.NotNull(FieldRules.WifiPassword(new string('a', 64), WifiSecurity.Wpa));

    [Theory]
    [InlineData("abcde")]
    [InlineData("abcdefghijklm")]
    [InlineData("0123456789")]
    [InlineData("0123456789abcdef01234567ab")]
    public void WepKey_AcceptsTheStandardLengths(string value) =>
        Assert.Null(FieldRules.WepKeyNote(value));

    /// <summary>
    /// A warning rather than an error, because routers do vary and blocking the save on a
    /// key we merely find surprising would be worse than letting it through with a note.
    /// </summary>
    [Fact]
    public void WepKey_NotesAnUnusualLength() => Assert.NotNull(FieldRules.WepKeyNote("abcdefg"));
}
