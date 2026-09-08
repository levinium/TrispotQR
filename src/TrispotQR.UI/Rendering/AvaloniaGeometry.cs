using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Rendering;

namespace TrispotQR.UI.Rendering;

/// <summary>
/// Converts the app's own geometry and colour types into Avalonia's.
///
/// The mirror image of the WPF adapter, and unlike that one this is not throwaway: it is how
/// every platform draws the live preview from here on. Export never comes through here. That
/// stays on Core's Skia rasteriser, which is what keeps a saved PNG identical across
/// operating systems rather than at the mercy of each toolkit's antialiasing.
/// </summary>
public static class AvaloniaGeometry
{
    public static Color ToColor(RgbColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    public static IBrush ToBrush(RgbColor c) => new ImmutableSolidColorBrush(ToColor(c));

    public static IPen? ToPen(QrStroke? stroke) =>
        stroke is null
            ? null
            : new ImmutablePen(
                new ImmutableSolidColorBrush(ToColor(stroke.Color)),
                stroke.Thickness,
                lineJoin: PenLineJoin.Round);

    public static StreamGeometry ToStreamGeometry(QrPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var geometry = new StreamGeometry();
        using var context = geometry.Open();

        context.SetFillRule(path.FillRule == QrFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero);

        foreach (var figure in path.Figures)
        {
            context.BeginFigure(ToPoint(figure.Start), isFilled: true);

            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case QrLineTo line:
                        context.LineTo(ToPoint(line.To));
                        break;

                    case QrArcTo arc:
                        // Always the small arc and never rotated: every corner this app draws
                        // is a quarter circle, which is what QrArcTo documents.
                        context.ArcTo(
                            ToPoint(arc.To),
                            new Size(arc.Radius, arc.Radius),
                            rotationAngle: 0,
                            isLargeArc: false,
                            arc.Clockwise ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                        break;

                    case QrCubicTo cubic:
                        context.CubicBezierTo(ToPoint(cubic.C1), ToPoint(cubic.C2), ToPoint(cubic.To));
                        break;

                    default:
                        throw new NotSupportedException($"Unhandled segment type {segment.GetType().Name}.");
                }
            }

            context.EndFigure(figure.IsClosed);
        }

        return geometry;
    }

    private static Point ToPoint(QrPoint p) => new(p.X, p.Y);
}
