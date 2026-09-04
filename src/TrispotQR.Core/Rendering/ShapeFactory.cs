using System.Windows;
using System.Windows.Media;

namespace TrispotQR.Core.Rendering;

/// <summary>
/// Builds the primitive figures the code is drawn from. Everything is expressed in module
/// units, and everything comes back as a <see cref="PathFigure"/> so layers can be
/// assembled into a single path geometry that both the renderer and the SVG exporter
/// understand without special cases.
/// </summary>
internal static class ShapeFactory
{
    /// <summary>
    /// A rectangle with an independent corner radius on each corner. Corners with a zero
    /// radius stay sharp. This one primitive covers squares, rounded squares, circles
    /// (all four radii at half the width), leaves and every fluid module.
    /// </summary>
    public static PathFigure RoundedRect(
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

        var figure = new PathFigure { StartPoint = new Point(x + topLeft, y), IsClosed = true, IsFilled = true };

        figure.Segments.Add(Line(right - topRight, y));
        AddCorner(figure, topRight, right, y + topRight);

        figure.Segments.Add(Line(right, bottom - bottomRight));
        AddCorner(figure, bottomRight, right - bottomRight, bottom);

        figure.Segments.Add(Line(x + bottomLeft, bottom));
        AddCorner(figure, bottomLeft, x, bottom - bottomLeft);

        figure.Segments.Add(Line(x, y + topLeft));
        AddCorner(figure, topLeft, x + topLeft, y);

        figure.Freeze();
        return figure;
    }

    /// <summary>A square with every corner rounded by the same amount.</summary>
    public static PathFigure RoundedRect(double x, double y, double size, double radius) =>
        RoundedRect(x, y, size, size, radius, radius, radius, radius);

    /// <summary>A circle inscribed in the given square, drawn as a fully rounded rectangle.</summary>
    public static PathFigure Circle(double x, double y, double size) =>
        RoundedRect(x, y, size, size, size / 2, size / 2, size / 2, size / 2);

    /// <summary>A square rotated 45 degrees, with its points touching the middle of each edge.</summary>
    public static PathFigure Diamond(double x, double y, double size)
    {
        var half = size / 2;
        var figure = new PathFigure { StartPoint = new Point(x + half, y), IsClosed = true, IsFilled = true };
        figure.Segments.Add(Line(x + size, y + half));
        figure.Segments.Add(Line(x + half, y + size));
        figure.Segments.Add(Line(x, y + half));
        figure.Freeze();
        return figure;
    }

    private static void AddCorner(PathFigure figure, double radius, double endX, double endY)
    {
        if (radius <= 0)
        {
            // No arc needed. The following line segment already starts at this corner.
            return;
        }

        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(endX, endY),
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = false,
            RotationAngle = 0,
        });
    }

    private static LineSegment Line(double x, double y) => new(new Point(x, y), true);
}
