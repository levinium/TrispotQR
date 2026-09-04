using System.Text;
using System.Text.RegularExpressions;

namespace TrispotQR.Core.Payloads;

/// <summary>
/// Builds the payload strings behind the content type helpers. The escaping rules are
/// the part scanners are strict about and third-party generators most often get wrong,
/// so each format is covered by its own tests.
/// </summary>
public static partial class PayloadBuilder
{
    /// <summary>
    /// Schemes that are written without a double slash. Everything else is recognised by
    /// the "://" itself, which matters because a bare "example.com:8080/path" would
    /// otherwise look like it already carried a scheme named "example.com".
    /// </summary>
    private static readonly string[] SchemelessPrefixes =
        ["mailto:", "tel:", "sms:", "smsto:", "geo:", "bitcoin:", "matmsg:"];

    /// <summary>
    /// Normalises a web address. A string that already carries a scheme is left exactly
    /// as typed; anything else gets https:// so a bare domain still opens a browser.
    /// </summary>
    public static string Url(string? input)
    {
        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        return HasScheme(text) ? text : $"https://{text}";
    }

    private static bool HasScheme(string text) =>
        text.Contains("://", StringComparison.Ordinal)
        || SchemelessPrefixes.Any(p => text.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Normalises a web address and says whether it actually looks like one.
    ///
    /// This exists because the most common way a QR code fails is not a rendering problem
    /// at all: the code encodes bare text like "example.org", which scans perfectly and
    /// then does nothing useful because the phone has no scheme to open. Adding https://
    /// silently fixes that, and the warning catches the rest.
    /// </summary>
    public static UrlCheck CheckUrl(string? input)
    {
        var text = (input ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return new UrlCheck(string.Empty, false, "Enter a web address, for example example.org/tickets.");
        }

        var normalised = Url(text);
        var addedScheme = !ReferenceEquals(normalised, text) && normalised != text;

        if (!Uri.TryCreate(normalised, UriKind.Absolute, out var uri))
        {
            return new UrlCheck(normalised, false,
                "That does not look like a valid web address. Check for typos or stray spaces.");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return new UrlCheck(normalised, false,
                $"That starts with \"{uri.Scheme}:\", which is not a web address. A link should start with "
                + "https:// or be a plain domain like example.org.");
        }

        if (text.Any(char.IsWhiteSpace))
        {
            return new UrlCheck(normalised, false,
                "A web address cannot contain spaces. Remove them, or switch the type to Plain text.");
        }

        // Uri.TryCreate is happy with a hostname that no browser could resolve, so the
        // shape of the host is checked separately. Localhost and bare IPs are allowed
        // because they are legitimate on an internal network.
        var host = uri.Host;
        var plausible = HostNameRegex().IsMatch(host)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6;

        if (!plausible)
        {
            return new UrlCheck(normalised, false,
                $"\"{host}\" does not look like a website name. It usually needs a dot and an ending "
                + "such as .org or .com.");
        }

        var note = addedScheme
            ? $"Saved as {normalised} so phones open it as a link."
            : null;

        return new UrlCheck(normalised, true, note);
    }

    /// <summary>
    /// Builds a WIFI: payload. Phones join the network straight from the scan, so the
    /// reserved characters have to be escaped or the fields run together.
    /// </summary>
    public static string Wifi(string ssid, string? password, WifiSecurity security, bool hidden)
    {
        var type = security switch
        {
            WifiSecurity.Wpa => "WPA",
            WifiSecurity.Wep => "WEP",
            _ => "nopass",
        };

        var builder = new StringBuilder("WIFI:");
        builder.Append("T:").Append(type).Append(';');
        builder.Append("S:").Append(EscapeWifi(ssid)).Append(';');

        if (security != WifiSecurity.None && !string.IsNullOrEmpty(password))
        {
            builder.Append("P:").Append(EscapeWifi(password)).Append(';');
        }

        if (hidden)
        {
            builder.Append("H:true;");
        }

        return builder.Append(';').ToString();
    }

    /// <summary>Builds a mailto: payload, percent-encoding the optional subject and body.</summary>
    public static string Email(string address, string? subject, string? body)
    {
        var result = new StringBuilder("mailto:").Append(address.Trim());
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(subject))
        {
            parts.Add("subject=" + Uri.EscapeDataString(subject));
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            parts.Add("body=" + Uri.EscapeDataString(body));
        }

        if (parts.Count > 0)
        {
            result.Append('?').Append(string.Join("&", parts));
        }

        return result.ToString();
    }

    /// <summary>Builds a tel: payload from a number typed in any common format.</summary>
    public static string Phone(string number) => "tel:" + NormaliseNumber(number);

    /// <summary>Builds an SMSTO: payload. The message is optional.</summary>
    public static string Sms(string number, string? message)
    {
        var target = NormaliseNumber(number);
        return string.IsNullOrWhiteSpace(message) ? $"SMSTO:{target}" : $"SMSTO:{target}:{message}";
    }

    /// <summary>
    /// Builds a vCard 3.0 payload. Version 3.0 rather than 4.0 because iOS and Android
    /// both import 3.0 reliably, which is not true of 4.0.
    /// </summary>
    public static string VCard(ContactCard card)
    {
        var lines = new List<string> { "BEGIN:VCARD", "VERSION:3.0" };

        var first = card.FirstName?.Trim() ?? string.Empty;
        var last = card.LastName?.Trim() ?? string.Empty;

        if (first.Length > 0 || last.Length > 0)
        {
            // Structured name: Family;Given;Additional;Prefix;Suffix. The separators are
            // structural, so only the values inside each component get escaped.
            lines.Add($"N:{EscapeVCard(last)};{EscapeVCard(first)};;;");
            lines.Add($"FN:{EscapeVCard(string.Join(" ", new[] { first, last }.Where(p => p.Length > 0)))}");
        }

        AddIfPresent(lines, "ORG:", card.Organization);
        AddIfPresent(lines, "TITLE:", card.Title);

        if (!string.IsNullOrWhiteSpace(card.Phone))
        {
            lines.Add("TEL;TYPE=CELL:" + NormaliseNumber(card.Phone));
        }

        AddIfPresent(lines, "EMAIL:", card.Email);

        if (!string.IsNullOrWhiteSpace(card.Website))
        {
            lines.Add("URL:" + Url(card.Website));
        }

        lines.Add("END:VCARD");
        return string.Join("\r\n", lines);
    }

    private static void AddIfPresent(List<string> lines, string prefix, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add(prefix + EscapeVCard(value.Trim()));
        }
    }

    /// <summary>
    /// Reduces a typed number to E.164 where we can be confident. A plus sign is taken at
    /// face value; a bare ten digit number is assumed to be North American, which is the
    /// right guess for this office. Anything else is passed through as digits so we never
    /// invent a country code we cannot justify.
    /// </summary>
    private static string NormaliseNumber(string number)
    {
        var text = (number ?? string.Empty).Trim();
        var explicitPlus = text.StartsWith('+');
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());

        if (explicitPlus)
        {
            return "+" + digits;
        }

        return digits.Length switch
        {
            10 => "+1" + digits,
            11 when digits[0] == '1' => "+" + digits,
            _ => digits,
        };
    }

    private static string EscapeWifi(string value) => Escape(value, ";,:\"");

    private static string EscapeVCard(string value) => Escape(value, ";,").Replace("\n", "\\n");

    /// <summary>
    /// Prefixes a backslash to the backslash itself and to every character in
    /// <paramref name="reserved"/>. The backslash goes first so it is not applied twice.
    /// </summary>
    private static string Escape(string value, string reserved)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            if (c == '\\' || reserved.Contains(c))
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"^(?=.{1,253}$)([a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$")]
    private static partial Regex HostNameRegex();
}
