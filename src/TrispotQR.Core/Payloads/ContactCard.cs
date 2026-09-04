namespace TrispotQR.Core.Payloads;

/// <summary>Wi-Fi authentication type, as understood by the WIFI: payload format.</summary>
public enum WifiSecurity
{
    /// <summary>Open network. No password is written into the payload.</summary>
    None,

    /// <summary>WPA or WPA2 personal. Covers nearly every modern network.</summary>
    Wpa,

    /// <summary>Legacy WEP.</summary>
    Wep,
}

/// <summary>Fields for the contact card content type. Every field is optional.</summary>
public sealed class ContactCard
{
    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? Organization { get; init; }

    public string? Title { get; init; }

    public string? Phone { get; init; }

    public string? Email { get; init; }

    public string? Website { get; init; }
}
