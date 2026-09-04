using TrispotQR.Core.Primitives;

namespace TrispotQR.Core.Styling;

/// <summary>
/// A colour expressed as hue, saturation and value, which is the model a
/// saturation/value square and a hue strip are drawn in.
///
/// Worth knowing before relying on a round trip: the conversion from RGB is lossy at two
/// edges. Every grey has a saturation of zero, which leaves its hue undefined, and black
/// has a value of zero, which leaves both its hue and its saturation undefined. This type
/// reports zero in those cases rather than inventing a value. A picker that wants the
/// crosshair to stay put when the user drags into black has to remember the hue and
/// saturation itself; see how ColorPicker does it.
/// </summary>
/// <param name="Hue">Degrees around the colour wheel, 0 to 360. Red is 0.</param>
/// <param name="Saturation">0 for grey through to 1 for a fully saturated colour.</param>
/// <param name="Value">0 for black through to 1 for the brightest form of the hue.</param>
public readonly record struct HsvColor(double Hue, double Saturation, double Value)
{
    public static HsvColor FromRgb(RgbColor colour)
    {
        var r = colour.R / 255.0;
        var g = colour.G / 255.0;
        var b = colour.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        var value = max;
        var saturation = max <= 0 ? 0 : delta / max;

        if (delta <= 0)
        {
            // A grey. Hue genuinely has no meaning here, so report zero rather than a
            // number that would look authoritative and move a crosshair for no reason.
            return new HsvColor(0, saturation, value);
        }

        double hue;

        if (max == r)
        {
            hue = 60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            hue = 60 * (((b - r) / delta) + 2);
        }
        else
        {
            hue = 60 * (((r - g) / delta) + 4);
        }

        if (hue < 0)
        {
            hue += 360;
        }

        return new HsvColor(hue, saturation, value);
    }

    /// <summary>
    /// Builds an RGB colour. Hue wraps, so 360 and 0 are the same and negatives are
    /// accepted; saturation and value are clamped.
    /// </summary>
    public static RgbColor ToRgb(double hue, double saturation, double value)
    {
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        hue %= 360;
        if (hue < 0)
        {
            hue += 360;
        }

        var c = value * saturation;
        var x = c * (1 - Math.Abs(((hue / 60) % 2) - 1));
        var m = value - c;

        var (r, g, b) = (int)(hue / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return RgbColor.FromRgb(Channel(r + m), Channel(g + m), Channel(b + m));
    }

    public RgbColor ToRgb() => ToRgb(Hue, Saturation, Value);

    private static byte Channel(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
}
