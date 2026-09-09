using TrispotQR.Core.Primitives;
using TrispotQR.Core.Styling;

namespace TrispotQR.UI.Controls;

/// <summary>
/// The one piece of HSV arithmetic the picker needs that
/// <see cref="TrispotQR.Core.Styling.HsvColor"/> does not already have.
///
/// The plan for this task called for a fresh <c>TrispotQR.UI.Controls.HsvColor</c>, but
/// Core has carried that exact type since the WPF picker was written, tested to a full
/// 4,096 colour round trip, and the WPF picker consumes it. Copying it here would fork
/// the arithmetic and put a second <c>HsvColor</c> in scope for every file in this
/// namespace, shadowing the real one. So the Core type stays the single definition and
/// only the missing alpha-aware conversion lives here.
/// </summary>
public static class HsvColorExtensions
{
    /// <summary>
    /// Builds an RGB colour from hue, saturation and value, carrying the alpha the caller
    /// supplies. HSV has no alpha of its own, so the picker has to pass through whatever
    /// the colour it is editing already had.
    /// </summary>
    public static RgbColor ToRgb(this HsvColor hsv, byte alpha)
    {
        var rgb = hsv.ToRgb();
        return RgbColor.FromArgb(alpha, rgb.R, rgb.G, rgb.B);
    }
}
