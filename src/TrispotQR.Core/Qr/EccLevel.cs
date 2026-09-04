namespace TrispotQR.Core.Qr;

/// <summary>
/// QR error correction level, ordered weakest to strongest so the values can be compared
/// with &gt; and &lt; when checking whether the encoder boosted the level.
/// </summary>
public enum EccLevel
{
    /// <summary>Recovers about 7% damage. Smallest, densest-looking code.</summary>
    Low = 0,

    /// <summary>Recovers about 15% damage. The sensible default.</summary>
    Medium = 1,

    /// <summary>Recovers about 25% damage.</summary>
    Quartile = 2,

    /// <summary>Recovers about 30% damage. Needed when a logo covers the middle.</summary>
    High = 3,
}
