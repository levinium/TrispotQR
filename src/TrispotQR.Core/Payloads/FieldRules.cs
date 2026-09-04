using System.Text;
using System.Text.RegularExpressions;

namespace TrispotQR.Core.Payloads;

/// <summary>
/// What counts as usable input for a single field, as pure functions returning the message
/// to show or null when there is nothing wrong.
///
/// Every rule other than <see cref="Required"/> passes a blank value. Emptiness is one
/// problem with one message, and a blank box reporting both "enter an email address" and
/// "that does not look like an email address" reads as though it is broken.
///
/// The messages say what to do rather than what happened, because the person reading them
/// is usually not the person who chose the format.
/// </summary>
public static partial class FieldRules
{
    /// <summary>Lengths, in characters, that a WEP key is normally written in.</summary>
    private static readonly int[] WepTextLengths = [5, 13, 16, 29];

    /// <summary>Lengths of a WEP key written as hex instead.</summary>
    private static readonly int[] WepHexLengths = [10, 26, 32, 58];

    /// <summary>
    /// Rejects a blank value. <paramref name="what"/> completes the sentence "Enter ...",
    /// so it reads as a noun phrase: "a network name", "the web address".
    /// </summary>
    public static string? Required(string? value, string what) =>
        string.IsNullOrWhiteSpace(value) ? $"Enter {what}." : null;

    /// <summary>Checks an email address closely enough to catch a typo, and no closer.</summary>
    public static string? Email(string? value)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return null;
        }

        if (text.Any(char.IsWhiteSpace))
        {
            return "An email address cannot contain spaces.";
        }

        var parts = text.Split('@');

        if (parts.Length != 2)
        {
            return "An email address needs one @ in it, like name@example.org.";
        }

        if (parts[0].Length == 0)
        {
            return "Add the part before the @, like name@example.org.";
        }

        return DomainRegex().IsMatch(parts[1])
            ? null
            : "The part after the @ should be a domain like example.org.";
    }

    /// <summary>
    /// Checks a phone number. Deliberately permissive about punctuation, because people
    /// write numbers in many shapes and <see cref="PayloadBuilder"/> strips it all anyway.
    /// What it will not accept is something that cannot be dialled.
    /// </summary>
    public static string? Phone(string? value)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return null;
        }

        if (text.IndexOf('+', 1) > 0)
        {
            return "A plus sign can only appear at the start, before the country code.";
        }

        if (text.Any(c => !char.IsAsciiDigit(c) && !"+-() .".Contains(c)))
        {
            return "A phone number can only contain digits and + - ( ) or spaces.";
        }

        return text.Count(char.IsAsciiDigit) < 7
            ? "That is too short to be a phone number."
            : null;
    }

    /// <summary>
    /// Checks a web address, reusing the same logic as the Link content type so the two
    /// cannot disagree about what a website looks like.
    /// </summary>
    public static string? Website(string? value)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return null;
        }

        var check = PayloadBuilder.CheckUrl(text);
        return check.IsValid ? null : check.Message;
    }

    /// <summary>Checks a Wi-Fi network name against the length the standard allows.</summary>
    public static string? Ssid(string? value)
    {
        var text = value ?? string.Empty;

        if (text.Length == 0)
        {
            return null;
        }

        // 32 bytes rather than 32 characters: an accented or non-Latin name reaches the
        // limit sooner, and a router will silently truncate rather than tell anyone.
        return Encoding.UTF8.GetByteCount(text) > 32
            ? "A network name cannot be longer than 32 characters."
            : null;
    }

    /// <summary>
    /// Checks a Wi-Fi password for the chosen security type. An open network wants no
    /// password at all, so nothing is required there.
    /// </summary>
    public static string? WifiPassword(string? value, WifiSecurity security)
    {
        if (security == WifiSecurity.None)
        {
            return null;
        }

        var text = value ?? string.Empty;

        if (text.Length == 0)
        {
            return "Enter the password, or set Security to None for an open network.";
        }

        if (security != WifiSecurity.Wpa)
        {
            return null;
        }

        return text.Length switch
        {
            < 8 => "A WPA password is at least 8 characters.",
            > 63 => "A WPA password is at most 63 characters.",
            _ => null,
        };
    }

    /// <summary>
    /// Comments on the length of a WEP key. Separate from <see cref="WifiPassword"/> and
    /// only ever a warning: WEP keys come in a handful of standard lengths, but equipment
    /// varies enough that refusing to save an unusual one would be presumptuous.
    /// </summary>
    public static string? WepKeyNote(string? value)
    {
        var text = value ?? string.Empty;

        if (text.Length == 0)
        {
            return null;
        }

        var isHex = text.All(char.IsAsciiHexDigit);
        var expected = isHex ? WepHexLengths : WepTextLengths;

        if (isHex && WepTextLengths.Contains(text.Length))
        {
            return null;
        }

        return expected.Contains(text.Length)
            ? null
            : "That is an unusual length for a WEP key. Check it against the router.";
    }

    [GeneratedRegex(@"^(?=.{1,253}$)([a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$")]
    private static partial Regex DomainRegex();
}
