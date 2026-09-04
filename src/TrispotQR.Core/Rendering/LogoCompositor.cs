using System.Windows;
using System.Windows.Media;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Styling;

namespace TrispotQR.Core.Rendering;

/// <summary>
/// Places a logo in the middle of a code and clears the modules underneath it.
///
/// Punching a hole rather than covering the modules is what keeps the result readable:
/// the error correction has to reconstruct the missing data either way, and a clean
/// bounded gap with margin around it is far easier for a decoder than a logo blended into
/// the pattern. The scannability check then confirms the loss was actually survivable.
/// </summary>
internal static class LogoCompositor
{
    /// <summary>
    /// Works out where the logo sits, in module units, fitted inside its box without
    /// distorting the image. Returns null when there is no logo or the file cannot be read,
    /// which is what makes an unreadable file degrade into a plain code rather than a crash.
    /// </summary>
    public static LogoPlacement? Place(QrMatrix matrix, QrStyle style, int quiet)
    {
        if (!style.Logo.HasImage)
        {
            return null;
        }

        var image = QrRenderer.LoadImage(style.Logo.Path!);
        if (image is null || image.PixelWidth == 0 || image.PixelHeight == 0)
        {
            return null;
        }

        var box = style.Logo.SizeRatio * matrix.Size;
        var aspect = (double)image.PixelWidth / image.PixelHeight;

        var width = aspect >= 1 ? box : box * aspect;
        var height = aspect >= 1 ? box / aspect : box;

        var centre = quiet + (matrix.Size / 2.0);
        var placement = new LogoPlacement(
            style.Logo.Path!,
            centre - (width / 2),
            centre - (height / 2),
            width,
            height);

        var punch = PunchRect(placement, style);
        var coverage = punch.Width * punch.Height / (matrix.Size * (double)matrix.Size);

        return placement with { CoverageRatio = coverage };
    }

    /// <summary>
    /// The area cleared behind the logo: its box plus the configured padding on every
    /// side. Null when the style asks for no punch at all.
    /// </summary>
    public static Geometry? Punch(LogoPlacement placement, QrStyle style)
    {
        if (style.Logo.PunchShape == LogoPunchShape.None)
        {
            return null;
        }

        var rect = PunchRect(placement, style);
        var geometry = new PathGeometry { FillRule = FillRule.Nonzero };

        geometry.Figures.Add(style.Logo.PunchShape switch
        {
            LogoPunchShape.Circle => ShapeFactory.RoundedRect(
                rect.X, rect.Y, rect.Width, rect.Height,
                rect.Width / 2, rect.Width / 2, rect.Width / 2, rect.Width / 2),

            LogoPunchShape.RoundedSquare => ShapeFactory.RoundedRect(
                rect.X, rect.Y, rect.Width, rect.Height,
                RoundedRadius(rect), RoundedRadius(rect), RoundedRadius(rect), RoundedRadius(rect)),

            _ => ShapeFactory.RoundedRect(rect.X, rect.Y, rect.Width, rect.Height, 0, 0, 0, 0),
        });

        geometry.Freeze();
        return geometry;
    }

    private static Rect PunchRect(LogoPlacement placement, QrStyle style)
    {
        var padding = style.Logo.PunchPadding;
        return new Rect(
            placement.X - padding,
            placement.Y - padding,
            placement.Width + (padding * 2),
            placement.Height + (padding * 2));
    }

    private static double RoundedRadius(Rect rect) => Math.Min(rect.Width, rect.Height) * 0.18;
}
