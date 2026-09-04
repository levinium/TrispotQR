using TrispotQR.Core.Primitives;
using TrispotQR.Core.Rendering;

namespace TrispotQR.Tests;

public class ShapeFactoryTests
{
    [Fact]
    public void SharpRectangle_IsFourLinesAndNoArcs()
    {
        var figure = ShapeFactory.RoundedRect(0, 0, 10, 0);

        Assert.Equal(new QrPoint(0, 0), figure.Start);
        Assert.True(figure.IsClosed);
        Assert.Equal(4, figure.Segments.Count);
        Assert.All(figure.Segments, s => Assert.IsType<QrLineTo>(s));
    }

    [Fact]
    public void RoundedRectangle_AlternatesLinesAndArcs()
    {
        var figure = ShapeFactory.RoundedRect(0, 0, 10, 2);

        Assert.Equal(8, figure.Segments.Count);
        Assert.Equal(4, figure.Segments.OfType<QrArcTo>().Count());
        Assert.All(figure.Segments.OfType<QrArcTo>(), a =>
        {
            Assert.Equal(2, a.Radius);
            Assert.True(a.Clockwise);
        });
    }

    /// <summary>
    /// A radius larger than half the shorter side would make opposite corners overlap and
    /// the arcs fold back on themselves.
    /// </summary>
    [Fact]
    public void Radius_IsClampedToHalfTheShorterSide()
    {
        var figure = ShapeFactory.RoundedRect(0, 0, 10, 4, 99, 99, 99, 99);
        Assert.All(figure.Segments.OfType<QrArcTo>(), a => Assert.Equal(2, a.Radius));
    }

    [Fact]
    public void Circle_IsARectangleRoundedByHalfItsSize()
    {
        var figure = ShapeFactory.Circle(0, 0, 8);
        Assert.All(figure.Segments.OfType<QrArcTo>(), a => Assert.Equal(4, a.Radius));
    }

    [Fact]
    public void Diamond_TouchesTheMiddleOfEachEdge()
    {
        var figure = ShapeFactory.Diamond(0, 0, 10);

        Assert.Equal(new QrPoint(5, 0), figure.Start);
        Assert.Equal(
            [new QrPoint(10, 5), new QrPoint(5, 10), new QrPoint(0, 5)],
            figure.Segments.Cast<QrLineTo>().Select(s => s.To));
    }

    [Fact]
    public void Offsets_ArePlacedWhereAsked() =>
        Assert.Equal(new QrPoint(3, 7), ShapeFactory.RoundedRect(3, 7, 2, 0).Start);
}
