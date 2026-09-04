using System.Globalization;
using System.Text;
using TrispotQR.Core.Primitives;

namespace TrispotQR.Core.Export;

/// <summary>
/// Serialises a <see cref="QrPath"/> to the SVG <c>d</c> attribute.
///
/// The previous version took WPF's path mini-language from Geometry.ToString and stripped
/// its leading fill-rule token. That worked, and it depended on the debug-string format of
/// a Windows-only type. Writing the data is a dozen lines and owes nothing to anything.
/// </summary>
internal static class SvgPathData
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string FillRule(QrFillRule rule) => rule == QrFillRule.EvenOdd ? "evenodd" : "nonzero";

    public static string ToData(QrPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var builder = new StringBuilder();

        foreach (var figure in path.Figures)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append('M').Append(Pair(figure.Start));

            foreach (var segment in figure.Segments)
            {
                builder.Append(' ');

                switch (segment)
                {
                    case QrLineTo line:
                        builder.Append('L').Append(Pair(line.To));
                        break;

                    case QrArcTo arc:
                        builder.Append('A')
                            .Append(Num(arc.Radius)).Append(',').Append(Num(arc.Radius))
                            .Append(" 0 0 ")
                            .Append(arc.Clockwise ? '1' : '0')
                            .Append(' ')
                            .Append(Pair(arc.To));
                        break;

                    case QrCubicTo cubic:
                        builder.Append('C')
                            .Append(Pair(cubic.C1)).Append(' ')
                            .Append(Pair(cubic.C2)).Append(' ')
                            .Append(Pair(cubic.To));
                        break;
                }
            }

            if (figure.IsClosed)
            {
                builder.Append(" Z");
            }
        }

        return builder.ToString();
    }

    private static string Pair(QrPoint point) => $"{Num(point.X)},{Num(point.Y)}";

    private static string Num(double value) => value.ToString("0.####", Invariant);
}
