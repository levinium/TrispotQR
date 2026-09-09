using System.Globalization;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Styling;

namespace TrispotQR.UI.Controls;

/// <summary>
/// Everything a colour picker does that does not need a window.
///
/// The picker offers four ways in, all equal: the saturation/value square with its hue
/// strip, a palette, a hex box and RGB sliders. They stay in step because they all write
/// through this one hub and read <see cref="Color"/> back out. Keeping the hub here rather
/// than in the control means the arithmetic can be tested without a toolkit, and leaves
/// the control with only the part that genuinely needs one: pointers, layout and paint.
///
/// There is deliberately no reference to any UI framework in this file.
/// </summary>
public sealed class ColorPickerState
{
    /// <summary>
    /// Hue, saturation and value are held here rather than derived from <see cref="Color"/>
    /// every time, because that conversion is lossy at two edges: every grey has an
    /// undefined hue, and black has an undefined hue and saturation. Deriving them would
    /// make the crosshair jump to a corner and the hue strip snap to red the moment someone
    /// dragged the brightness to zero, and dragging back up would return red instead of the
    /// colour they started from.
    /// </summary>
    private double _hue;
    private double _saturation;
    private double _value;

    private RgbColor _color = RgbColor.Black;

    /// <summary>
    /// Set while the square or the hue strip is driving the change. Their own values are
    /// already exact, so re-deriving them from the rounded 8 bit colour would let the
    /// marker drift under the cursor during a drag.
    /// </summary>
    private bool _fromHsv;

    /// <summary>
    /// The selected colour. Assigning it is how a colour arrives from anywhere other than
    /// the square and the strip: a palette swatch, the RGB sliders, or whatever the control
    /// is bound to.
    /// </summary>
    public RgbColor Color
    {
        get => _color;
        set
        {
            _color = value;

            if (!_fromHsv)
            {
                AdoptHsvFrom(value);
            }
        }
    }

    /// <summary>Degrees around the colour wheel, 0 to 360. Red is 0.</summary>
    public double Hue => _hue;

    /// <summary>0 for grey through to 1 for a fully saturated colour.</summary>
    public double Saturation => _saturation;

    /// <summary>0 for black through to 1 for the brightest form of the hue.</summary>
    public double Value => _value;

    /// <summary>
    /// Selects a point in the saturation/value square, both 0 to 1. Out of range
    /// coordinates are clamped rather than ignored, so a drag that overshoots the edge of
    /// the control pins to it instead of stopping dead.
    /// </summary>
    public void SetSaturationValue(double saturation, double value)
    {
        _saturation = Math.Clamp(saturation, 0, 1);
        _value = Math.Clamp(value, 0, 1);
        CommitHsv();
    }

    /// <summary>
    /// Selects a hue in degrees. Wraps rather than clamps, so 360 and 0 mean the same thing
    /// and -10 means 350: the strip is a wheel cut open, and dragging past either end should
    /// come round rather than stick.
    /// </summary>
    public void SetHue(double degrees)
    {
        _hue = ((degrees % 360) + 360) % 360;
        CommitHsv();
    }

    /// <summary>
    /// Takes what people actually type: with or without the hash, three digits or six, any
    /// case, with surrounding space. Returns false and leaves the colour alone for anything
    /// else, because someone half way through typing is not an error and should not see the
    /// picker snap to black.
    /// </summary>
    public bool TrySetHex(string? text)
    {
        if (text is null)
        {
            return false;
        }

        var digits = text.Trim().TrimStart('#');

        if (digits.Length == 3)
        {
            // #F0A is shorthand for #FF00AA: each digit doubles.
            digits = string.Concat(digits.Select(c => new string(c, 2)));
        }

        // AllowHexSpecifier on its own, without the surrounding-whitespace flags that
        // NumberStyles.HexNumber also carries: the length has already been checked, so a
        // space among the six characters would otherwise be silently read as a leading zero.
        if (digits.Length != 6 ||
            !uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        // Eight digit ARGB is not accepted, matching the WPF box. Alpha is not one of the
        // picker's controls, so it comes from the colour being edited, not from the text.
        Color = RgbColor.FromArgb(_color.A, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    /// <summary>
    /// Publishes the current hue, saturation and value as the selected colour. The write
    /// goes through <see cref="Color"/> so that anything watching it sees the change, but
    /// <see cref="_fromHsv"/> stops it being read straight back in and rounded.
    /// </summary>
    private void CommitHsv()
    {
        _fromHsv = true;

        try
        {
            Color = new HsvColor(_hue, _saturation, _value).ToRgb(_color.A);
        }
        finally
        {
            _fromHsv = false;
        }
    }

    /// <summary>
    /// Takes hue, saturation and value from a colour that arrived from somewhere else.
    ///
    /// The rule that looks like a bug and is not: components the incoming colour cannot
    /// define are left as they were. Every grey has a saturation of zero, which leaves its
    /// hue meaningless, and black has a value of zero, which leaves both meaningless. If
    /// those were copied across anyway, every trip down to black would reset the hue to red,
    /// and coming back up would return red rather than the colour the user was working on.
    /// A colour that genuinely carries a hue does override the retained one; only a grey
    /// leaves it alone.
    /// </summary>
    private void AdoptHsvFrom(RgbColor colour)
    {
        var hsv = HsvColor.FromRgb(colour);

        if (hsv.Saturation > 0)
        {
            _hue = hsv.Hue;
        }

        if (hsv.Value > 0)
        {
            _saturation = hsv.Saturation;
        }

        _value = hsv.Value;
    }
}
