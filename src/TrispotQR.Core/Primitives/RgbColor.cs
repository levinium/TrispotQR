using System.Globalization;

namespace TrispotQR.Core.Primitives;

/// <summary>
/// A colour, with no dependency on any UI framework.
///
/// This replaces System.Windows.Media.Color, the single thing that kept most of this
/// project's styling code tied to Windows. Channel order matches the old type (A, R, G, B)
/// so the swap reads the same at every call site.
/// </summary>
public readonly record struct RgbColor(byte A, byte R, byte G, byte B)
{
    public static readonly RgbColor Black = FromRgb(0, 0, 0);
    public static readonly RgbColor White = FromRgb(255, 255, 255);
    public static readonly RgbColor Transparent = FromArgb(0, 255, 255, 255);

    /// <summary>The handful of names a hand-edited preset file might reasonably contain.</summary>
    private static readonly Dictionary<string, RgbColor> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = Black,
        ["white"] = White,
        ["transparent"] = Transparent,
        ["red"] = FromRgb(255, 0, 0),
        ["green"] = FromRgb(0, 128, 0),
        ["blue"] = FromRgb(0, 0, 255),
        ["gray"] = FromRgb(128, 128, 128),
        ["grey"] = FromRgb(128, 128, 128),
    };

    public static RgbColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    public static RgbColor FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);

    /// <summary>True when this colour would paint nothing at all.</summary>
    public bool IsTransparent => A == 0;

    /// <summary>
    /// The format the preset file has always used: alpha is written only when it is not
    /// fully opaque, so the common case stays a readable six-digit hex.
    /// </summary>
    public string ToHex() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    public static RgbColor Parse(string text) =>
        TryParse(text, out var colour) ? colour : throw new FormatException($"\"{text}\" is not a colour.");

    public static bool TryParse(string? text, out RgbColor colour)
    {
        colour = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        if (Named.TryGetValue(trimmed, out colour))
        {
            return true;
        }

        if (trimmed[0] != '#')
        {
            return false;
        }

        var digits = trimmed[1..];

        if (!uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        switch (digits.Length)
        {
            case 6:
                colour = FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
                return true;
            case 8:
                colour = FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
                return true;
            default:
                return false;
        }
    }
}
