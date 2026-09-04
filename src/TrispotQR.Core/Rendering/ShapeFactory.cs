using TrispotQR.Core.Primitives;

namespace TrispotQR.Core.Rendering;

/// <summary>
/// Builds the primitive figures the code is drawn from. Everything is expressed in module
/// units and comes back as a <see cref="QrFigure"/>, so a layer can be assembled into one
/// path that the rasteriser and the SVG writer both understand without special cases.
/// </summary>
internal static class ShapeFactory
{
    /// <summary>
    /// A rectangle with an independent corner radius on each corner. Corners with a zero
    /// radius stay sharp. This one primitive covers squares, rounded squares, circles
    /// (all four radii at half the width), leaves and every fluid module.
    /// </summary>
    public static QrFigure RoundedRect(
        double x, double y, double width, double height,
        double topLeft, double topRight, double bottomRight, double bottomLeft)
    {
        // A radius can never exceed half the shorter side, or opposite corners overlap
        // and the arcs fold back on themselves.
        var limit = Math.Min(width, height) / 2.0;
        topLeft = Math.Clamp(topLeft, 0, limit);
        topRight = Math.Clamp(topRight, 0, limit);
        bottomRight = Math.Clamp(bottomRight, 0, limit);
        bottomLeft = Math.Clamp(bottomLeft, 0, limit);

        var right = x + width;
        var bottom = y + height;
        var segments = new List<QrSegment>(8);

        segments.Add(new QrLineTo(new QrPoint(right - topRight, y)));
        AddCorner(segments, topRight, right, y + topRight);

        segments.Add(new QrLineTo(new QrPoint(right, bottom - bottomRight)));
        AddCorner(segments, bottomRight, right - bottomRight, bottom);

        segments.Add(new QrLineTo(new QrPoint(x + bottomLeft, bottom)));
        AddCorner(segments, bottomLeft, x, bottom - bottomLeft);

        segments.Add(new QrLineTo(new QrPoint(x, y + topLeft)));
        AddCorner(segments, topLeft, x + topLeft, y);

        return new QrFigure(new QrPoint(x + topLeft, y), segments, IsClosed: true);
    }

    /// <summary>A square with every corner rounded by the same amount.</summary>
    public static QrFigure RoundedRect(double x, double y, double size, double radius) =>
        RoundedRect(x, y, size, size, radius, radius, radius, radius);

    /// <summary>A circle inscribed in the given square, drawn as a fully rounded rectangle.</summary>
    public static QrFigure Circle(double x, double y, double size) =>
        RoundedRect(x, y, size, size, size / 2, size / 2, size / 2, size / 2);

    /// <summary>A square rotated 45 degrees, with its points touching the middle of each edge.</summary>
    public static QrFigure Diamond(double x, double y, double size)
    {
        var half = size / 2;

        return new QrFigure(
            new QrPoint(x + half, y),
            [
                new QrLineTo(new QrPoint(x + size, y + half)),
                new QrLineTo(new QrPoint(x + half, y + size)),
                new QrLineTo(new QrPoint(x, y + half)),
            ],
            IsClosed: true);
    }

    private static void AddCorner(List<QrSegment> segments, double radius, double endX, double endY)
    {
        if (radius <= 0)
        {
            // No arc needed. The following line segment already starts at this corner.
            return;
        }

        segments.Add(new QrArcTo(new QrPoint(endX, endY), radius, Clockwise: true));
    }
}
