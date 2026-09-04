using SkiaSharp;
using TrispotQR.Core.Primitives;

namespace TrispotQR.Core.Rendering;

/// <summary>
/// Converts between the app's own geometry model and Skia's.
///
/// The reverse direction exists for one reason: the logo punch-out is a boolean operation
/// only Skia can do, and its result has to come back into <see cref="QrPath"/> so the SVG
/// writer serialises exactly the shape the rasteriser paints.
/// </summary>
internal static class SkiaPath
{
    public static SKPath ToSKPath(QrPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var result = new SKPath
        {
            FillType = path.FillRule == QrFillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding,
        };

        foreach (var figure in path.Figures)
        {
            result.MoveTo((float)figure.Start.X, (float)figure.Start.Y);

            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case QrLineTo line:
                        result.LineTo((float)line.To.X, (float)line.To.Y);
                        break;

                    case QrArcTo arc:
                        result.ArcTo(
                            new SKPoint((float)arc.Radius, (float)arc.Radius),
                            0,
                            SKPathArcSize.Small,
                            arc.Clockwise ? SKPathDirection.Clockwise : SKPathDirection.CounterClockwise,
                            new SKPoint((float)arc.To.X, (float)arc.To.Y));
                        break;

                    case QrCubicTo cubic:
                        result.CubicTo(
                            (float)cubic.C1.X, (float)cubic.C1.Y,
                            (float)cubic.C2.X, (float)cubic.C2.Y,
                            (float)cubic.To.X, (float)cubic.To.Y);
                        break;
                }
            }

            if (figure.IsClosed)
            {
                result.Close();
            }
        }

        return result;
    }

    /// <summary>
    /// Reads a Skia path back into the model. Skia has already turned arcs into conics or
    /// cubics by this point, which is why <see cref="QrCubicTo"/> exists: the model has to
    /// express whatever comes back, or the punch-out would lose its rounded corners.
    /// </summary>
    public static QrPath ToQrPath(SKPath path, QrFillRule fillRule)
    {
        ArgumentNullException.ThrowIfNull(path);

        var figures = new List<QrFigure>();
        var segments = new List<QrSegment>();
        var start = new QrPoint();
        var open = false;

        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];

        while (true)
        {
            var verb = iterator.Next(points);

            if (verb == SKPathVerb.Done)
            {
                break;
            }

            switch (verb)
            {
                case SKPathVerb.Move:
                    Flush(figures, ref segments, start, open, closed: false);
                    start = Point(points[0]);
                    open = true;
                    break;

                case SKPathVerb.Line:
                    segments.Add(new QrLineTo(Point(points[1])));
                    break;

                case SKPathVerb.Quad:
                case SKPathVerb.Conic:
                    // Raised to a cubic so the model needs only one curve type. A conic is
                    // approximated; a quadratic converts exactly.
                    segments.Add(QuadToCubic(Point(points[0]), Point(points[1]), Point(points[2])));
                    break;

                case SKPathVerb.Cubic:
                    segments.Add(new QrCubicTo(Point(points[1]), Point(points[2]), Point(points[3])));
                    break;

                case SKPathVerb.Close:
                    Flush(figures, ref segments, start, open, closed: true);
                    open = false;
                    break;
            }
        }

        Flush(figures, ref segments, start, open, closed: false);
        return new QrPath(figures, fillRule);
    }

    private static void Flush(
        List<QrFigure> figures, ref List<QrSegment> segments, QrPoint start, bool open, bool closed)
    {
        if (!open || segments.Count == 0)
        {
            segments = [];
            return;
        }

        figures.Add(new QrFigure(start, segments, closed));
        segments = [];
    }

    /// <summary>A quadratic raised to an equivalent cubic, which is exact rather than approximate.</summary>
    private static QrCubicTo QuadToCubic(QrPoint from, QrPoint control, QrPoint to) =>
        new(
            new QrPoint(from.X + (2.0 / 3.0 * (control.X - from.X)), from.Y + (2.0 / 3.0 * (control.Y - from.Y))),
            new QrPoint(to.X + (2.0 / 3.0 * (control.X - to.X)), to.Y + (2.0 / 3.0 * (control.Y - to.Y))),
            to);

    private static QrPoint Point(SKPoint p) => new(p.X, p.Y);
}
