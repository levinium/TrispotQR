using SkiaSharp;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Rendering;

namespace TrispotQR.Tests;

public class SkiaPathTests
{
    /// <summary>
    /// Built by hand rather than through ShapeFactory: this task runs before ShapeFactory
    /// changes, and depending on it would couple two independent pieces of work.
    /// </summary>
    private static QrPath Square(double x, double y, double size, QrFillRule rule = QrFillRule.NonZero) =>
        new QrPathBuilder()
            .Add(new QrFigure(
                new QrPoint(x, y),
                [
                    new QrLineTo(new QrPoint(x + size, y)),
                    new QrLineTo(new QrPoint(x + size, y + size)),
                    new QrLineTo(new QrPoint(x, y + size)),
                ],
                IsClosed: true))
            .Build(rule);

    [Fact]
    public void ASquare_KeepsItsBoundsThroughSkia()
    {
        using var sk = SkiaPath.ToSKPath(Square(1, 2, 10));

        Assert.Equal(1f, sk.Bounds.Left, 3);
        Assert.Equal(2f, sk.Bounds.Top, 3);
        Assert.Equal(11f, sk.Bounds.Right, 3);
        Assert.Equal(12f, sk.Bounds.Bottom, 3);
    }

    [Fact]
    public void FillRule_IsCarriedAcross()
    {
        using var nonZero = SkiaPath.ToSKPath(Square(0, 0, 1));
        using var evenOdd = SkiaPath.ToSKPath(Square(0, 0, 1, QrFillRule.EvenOdd));

        Assert.Equal(SKPathFillType.Winding, nonZero.FillType);
        Assert.Equal(SKPathFillType.EvenOdd, evenOdd.FillType);
    }

    [Fact]
    public void AnArc_IsDrawnAsAnArc()
    {
        var path = new QrPathBuilder()
            .Add(new QrFigure(
                new QrPoint(0, 2),
                [new QrArcTo(new QrPoint(2, 0), 2, Clockwise: true)],
                IsClosed: false))
            .Build(QrFillRule.NonZero);

        using var sk = SkiaPath.ToSKPath(path);

        Assert.Equal(0f, sk.Bounds.Left, 2);
        Assert.Equal(2f, sk.Bounds.Right, 2);
        Assert.False(sk.IsEmpty);
    }

    /// <summary>
    /// The reverse trip exists for the logo punch-out, which is a Skia boolean op whose
    /// result has to come back into the model so SVG export sees the same shape.
    /// </summary>
    [Fact]
    public void RoundTrip_PreservesBounds()
    {
        var original = Square(3, 4, 6);
        using var sk = SkiaPath.ToSKPath(original);
        var back = SkiaPath.ToQrPath(sk, QrFillRule.NonZero);

        Assert.NotEmpty(back.Figures);

        using var again = SkiaPath.ToSKPath(back);
        Assert.Equal(sk.Bounds.Left, again.Bounds.Left, 3);
        Assert.Equal(sk.Bounds.Right, again.Bounds.Right, 3);
        Assert.Equal(sk.Bounds.Bottom, again.Bounds.Bottom, 3);
    }

    /// <summary>
    /// A straight-edged square round-trips fine even if a curve is mishandled, which is why
    /// this shape has to be an arc: Skia represents a true circular arc as a weighted conic,
    /// and dropping the weight during the reverse trip yields a parabola that still shares
    /// the arc's bounds but is no longer circular. Sampling points along the round-tripped
    /// curve and checking their distance from the arc's known centre catches that; a bounds
    /// check alone does not.
    /// </summary>
    [Fact]
    public void RoundTrip_KeepsAnArcCircular()
    {
        // A quarter circle of radius 2 between (0,2) and (2,0), sweeping clockwise, replaces
        // the sharp corner at (2,2): the arc bulges toward the origin, curving around a
        // centre at the corner it is rounding off, not at the origin.
        const double radius = 2;
        var centre = new QrPoint(2, 2);

        var path = new QrPathBuilder()
            .Add(new QrFigure(
                new QrPoint(0, 2),
                [new QrArcTo(new QrPoint(2, 0), radius, Clockwise: true)],
                IsClosed: false))
            .Build(QrFillRule.NonZero);

        using var sk = SkiaPath.ToSKPath(path);
        var back = SkiaPath.ToQrPath(sk, QrFillRule.NonZero);
        using var again = SkiaPath.ToSKPath(back);

        using var measure = new SKPathMeasure(again, forceClosed: false);
        var length = measure.Length;
        Assert.True(length > 0);

        for (var i = 0; i <= 10; i++)
        {
            var distance = length * i / 10f;
            Assert.True(measure.GetPosition(distance, out var point));

            var dx = point.X - (float)centre.X;
            var dy = point.Y - (float)centre.Y;
            var distanceFromCentre = Math.Sqrt((dx * dx) + (dy * dy));

            Assert.Equal(radius, distanceFromCentre, 1);
        }
    }

    [Fact]
    public void RoundTrip_KeepsTheFigureClosed()
    {
        using var sk = SkiaPath.ToSKPath(Square(0, 0, 1));
        var back = SkiaPath.ToQrPath(sk, QrFillRule.NonZero);

        Assert.True(back.Figures[0].IsClosed);
    }

    [Fact]
    public void AnEmptyPath_MakesAnEmptySKPath()
    {
        using var sk = SkiaPath.ToSKPath(QrPath.Empty);
        Assert.True(sk.IsEmpty);
    }
}
