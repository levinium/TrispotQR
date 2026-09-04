using System.Windows.Media;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Styling;

namespace TrispotQR.Core.Rendering;

/// <summary>
/// Turns a module matrix plus a style into a <see cref="QrDrawing"/>.
///
/// Two pieces of real work happen here. The three 7x7 finder patterns are lifted out of
/// the data modules so they can take their own shape and colour, and the fluid module
/// shape rounds each corner only where its two touching neighbours are light, which is
/// what makes runs of dark modules flow together instead of reading as separate blobs.
/// </summary>
public static class QrGeometryBuilder
{
    /// <summary>Corner radius of a rounded square module, as a fraction of the module.</summary>
    private const double RoundedModuleRadius = 0.30;

    /// <summary>Corner radius used by the fluid shape. Half a module turns an isolated one into a dot.</summary>
    private const double FluidRadius = 0.50;

    public static QrDrawing Build(QrMatrix matrix, QrStyle style)
    {
        style = style.Normalised();

        var quiet = style.QuietZoneModules;
        var sizeInUnits = matrix.Size + (quiet * 2.0);

        var layers = new List<QrLayer>
        {
            BuildModuleLayer(matrix, style, quiet),
            BuildMarkerFrameLayer(matrix, style, quiet),
            BuildMarkerCenterLayer(matrix, style, quiet),
        };

        var logo = LogoCompositor.Place(matrix, style, quiet);
        if (logo is not null && LogoCompositor.Punch(logo, style) is { } punch)
        {
            layers = layers.Select(layer => Punched(layer, punch)).ToList();
        }

        return new QrDrawing(sizeInUnits, style.Background, layers, logo);
    }

    /// <summary>
    /// Cuts the logo's clear area out of a layer. The boolean combine is only paid for
    /// when a logo is actually present.
    /// </summary>
    private static QrLayer Punched(QrLayer layer, Geometry punch)
    {
        var combined = Geometry.Combine(layer.Geometry, punch, GeometryCombineMode.Exclude, null);
        combined.Freeze();
        return new QrLayer(layer.Name, combined, layer.Fill, layer.Stroke);
    }

    private static QrLayer BuildModuleLayer(QrMatrix matrix, QrStyle style, int quiet)
    {
        var geometry = new PathGeometry { FillRule = FillRule.Nonzero };

        for (var y = 0; y < matrix.Size; y++)
        {
            for (var x = 0; x < matrix.Size; x++)
            {
                if (!matrix.IsDark(x, y) || matrix.IsFinderPattern(x, y))
                {
                    continue;
                }

                geometry.Figures.Add(ModuleFigure(matrix, style, x, y, quiet));
            }
        }

        var stroke = PenFor(style, OutlineTarget.Modules);
        return new QrLayer(QrLayerNames.Modules, Normalise(geometry), Brush(style.Foreground), stroke);
    }

    private static PathFigure ModuleFigure(QrMatrix matrix, QrStyle style, int x, int y, int quiet)
    {
        var scale = style.ModuleScale;
        var inset = (1.0 - scale) / 2.0;
        var left = x + quiet + inset;
        var top = y + quiet + inset;

        return style.ModuleShape switch
        {
            ModuleShape.Square => ShapeFactory.RoundedRect(left, top, scale, 0),
            ModuleShape.RoundedSquare => ShapeFactory.RoundedRect(left, top, scale, RoundedModuleRadius * scale),
            ModuleShape.Circle => ShapeFactory.Circle(left, top, scale),
            ModuleShape.Diamond => ShapeFactory.Diamond(left, top, scale),
            ModuleShape.Fluid => FluidFigure(matrix, x, y, left, top, scale),
            _ => ShapeFactory.RoundedRect(left, top, scale, 0),
        };
    }

    /// <summary>
    /// Rounds a corner only when both modules touching that corner are light. A module
    /// with no dark neighbours becomes a dot; a run of dark modules keeps its shared
    /// edges square and so merges into one continuous outline.
    /// </summary>
    private static PathFigure FluidFigure(QrMatrix matrix, int x, int y, double left, double top, double scale)
    {
        var up = IsJoined(matrix, x, y - 1);
        var down = IsJoined(matrix, x, y + 1);
        var leftNeighbour = IsJoined(matrix, x - 1, y);
        var rightNeighbour = IsJoined(matrix, x + 1, y);

        var radius = FluidRadius * scale;

        return ShapeFactory.RoundedRect(
            left, top, scale, scale,
            topLeft: up || leftNeighbour ? 0 : radius,
            topRight: up || rightNeighbour ? 0 : radius,
            bottomRight: down || rightNeighbour ? 0 : radius,
            bottomLeft: down || leftNeighbour ? 0 : radius);
    }

    /// <summary>
    /// A neighbour counts as joined only when it is dark and is itself a data module.
    /// Finder patterns are drawn on their own layer, so a data module sitting against one
    /// must round that corner rather than reach out to meet a shape that is not there.
    /// </summary>
    private static bool IsJoined(QrMatrix matrix, int x, int y) =>
        matrix.IsDark(x, y) && !matrix.IsFinderPattern(x, y);

    private static QrLayer BuildMarkerFrameLayer(QrMatrix matrix, QrStyle style, int quiet)
    {
        // Even-odd fill: the inner figure punches a hole in the outer one, giving the ring
        // without paying for a boolean geometry combine.
        var geometry = new PathGeometry { FillRule = FillRule.EvenOdd };

        foreach (var (ox, oy) in matrix.FinderOrigins)
        {
            var x = ox + quiet;
            var y = oy + quiet;
            var corner = OuterCorner(matrix, ox, oy);

            geometry.Figures.Add(FrameFigure(style.MarkerFrameShape, x, y, 7, corner));
            geometry.Figures.Add(FrameFigure(style.MarkerFrameShape, x + 1, y + 1, 5, corner));
        }

        var stroke = PenFor(style, OutlineTarget.Markers);
        return new QrLayer(
            QrLayerNames.MarkerFrames,
            Normalise(geometry),
            Brush(style.EffectiveMarkerFrameColor),
            stroke);
    }

    private static PathFigure FrameFigure(MarkerFrameShape shape, double x, double y, double size, Corner outer)
    {
        // Radii are kept proportional to the ring so the outer and inner outlines stay
        // concentric and the ring reads as an even thickness all the way round.
        var proportional = size / 4.0;

        return shape switch
        {
            MarkerFrameShape.Square => ShapeFactory.RoundedRect(x, y, size, 0),
            MarkerFrameShape.RoundedSquare => ShapeFactory.RoundedRect(x, y, size, proportional),
            MarkerFrameShape.Circle => ShapeFactory.Circle(x, y, size),
            MarkerFrameShape.Leaf => LeafFigure(x, y, size, proportional * 1.6, outer),
            _ => ShapeFactory.RoundedRect(x, y, size, 0),
        };
    }

    /// <summary>
    /// Three rounded corners and one square one. The square corner faces away from the
    /// centre of the code, so the three markers point outwards as a set.
    /// </summary>
    private static PathFigure LeafFigure(double x, double y, double size, double radius, Corner outer) =>
        ShapeFactory.RoundedRect(
            x, y, size, size,
            topLeft: outer == Corner.TopLeft ? 0 : radius,
            topRight: outer == Corner.TopRight ? 0 : radius,
            bottomRight: outer == Corner.BottomRight ? 0 : radius,
            bottomLeft: outer == Corner.BottomLeft ? 0 : radius);

    private static QrLayer BuildMarkerCenterLayer(QrMatrix matrix, QrStyle style, int quiet)
    {
        var geometry = new PathGeometry { FillRule = FillRule.Nonzero };

        foreach (var (ox, oy) in matrix.FinderOrigins)
        {
            // The solid core of a finder pattern is the 3x3 block two modules in.
            var x = ox + quiet + 2;
            var y = oy + quiet + 2;

            geometry.Figures.Add(style.MarkerCenterShape switch
            {
                MarkerCenterShape.Square => ShapeFactory.RoundedRect(x, y, 3, 0),
                MarkerCenterShape.RoundedSquare => ShapeFactory.RoundedRect(x, y, 3, 0.9),
                MarkerCenterShape.Circle => ShapeFactory.Circle(x, y, 3),
                _ => ShapeFactory.RoundedRect(x, y, 3, 0),
            });
        }

        var stroke = PenFor(style, OutlineTarget.Markers);
        return new QrLayer(
            QrLayerNames.MarkerCenters,
            Normalise(geometry),
            Brush(style.EffectiveMarkerCenterColor),
            stroke);
    }

    private enum Corner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    private static Corner OuterCorner(QrMatrix matrix, int ox, int oy)
    {
        var far = matrix.Size - 7;
        if (ox >= far)
        {
            return Corner.TopRight;
        }

        return oy >= far ? Corner.BottomLeft : Corner.TopLeft;
    }

    private static Pen? PenFor(QrStyle style, OutlineTarget part)
    {
        var outline = style.Outline;
        if (!outline.Enabled)
        {
            return null;
        }

        var applies = outline.Target == OutlineTarget.Both || outline.Target == part;
        if (!applies)
        {
            return null;
        }

        var pen = new Pen(Brush(outline.Color), outline.ThicknessRatio)
        {
            LineJoin = PenLineJoin.Round,
        };

        pen.Freeze();
        return pen;
    }

    /// <summary>
    /// Flattens a geometry to a plain path. Everything downstream, the WPF renderer and
    /// the SVG exporter alike, then works on one shape of data, and path mini-language
    /// serialisation is guaranteed to round-trip.
    /// </summary>
    private static Geometry Normalise(PathGeometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
