using System.Windows;
using System.Windows.Media;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Rendering;

namespace TrispotQR.App.Rendering;

/// <summary>
/// Converts the app's own geometry and colour types into WPF's.
///
/// THROWAWAY. This exists only so the existing WPF window keeps working while Core is
/// freed from WPF. Phase 2 replaces the window with Avalonia and deletes this file.
/// </summary>
internal static class WpfGeometryAdapter
{
    public static Color ToColor(RgbColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    public static Color? ToColor(RgbColor? c) => c is { } value ? ToColor(value) : null;

    /// <summary>
    /// The way back, for the single boundary where a WPF colour enters the model: the
    /// colour picker hands the view model a <see cref="Color"/> and the view model stores
    /// it into a <see cref="TrispotQR.Core.Styling.QrStyle"/>.
    /// </summary>
    public static RgbColor ToRgbColor(Color c) => RgbColor.FromArgb(c.A, c.R, c.G, c.B);

    public static RgbColor? ToRgbColor(Color? c) => c is { } value ? ToRgbColor(value) : null;

    public static Brush ToBrush(RgbColor c)
    {
        var brush = new SolidColorBrush(ToColor(c));
        brush.Freeze();
        return brush;
    }

    public static Pen? ToPen(QrStroke? stroke)
    {
        if (stroke is null)
        {
            return null;
        }

        var pen = new Pen(ToBrush(stroke.Color), stroke.Thickness) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }

    public static Geometry ToGeometry(QrPath path)
    {
        var geometry = new PathGeometry
        {
            FillRule = path.FillRule == QrFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.Nonzero,
        };

        foreach (var figure in path.Figures)
        {
            var wpf = new PathFigure
            {
                StartPoint = new Point(figure.Start.X, figure.Start.Y),
                IsClosed = figure.IsClosed,
                IsFilled = true,
            };

            foreach (var segment in figure.Segments)
            {
                wpf.Segments.Add(segment switch
                {
                    QrLineTo line => new LineSegment(new Point(line.To.X, line.To.Y), true),

                    QrArcTo arc => new ArcSegment
                    {
                        Point = new Point(arc.To.X, arc.To.Y),
                        Size = new Size(arc.Radius, arc.Radius),
                        SweepDirection = arc.Clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                        IsLargeArc = false,
                        RotationAngle = 0,
                    },

                    QrCubicTo cubic => new BezierSegment(
                        new Point(cubic.C1.X, cubic.C1.Y),
                        new Point(cubic.C2.X, cubic.C2.Y),
                        new Point(cubic.To.X, cubic.To.Y),
                        true),

                    _ => throw new NotSupportedException($"Unknown segment {segment.GetType().Name}"),
                });
            }

            wpf.Freeze();
            geometry.Figures.Add(wpf);
        }

        geometry.Freeze();
        return geometry;
    }
}
