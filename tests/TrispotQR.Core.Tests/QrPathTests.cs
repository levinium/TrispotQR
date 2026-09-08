using TrispotQR.Core.Primitives;

namespace TrispotQR.Tests;

public class QrPathTests
{
    private static QrFigure Square(double x, double y, double size) =>
        new(new QrPoint(x, y),
        [
            new QrLineTo(new QrPoint(x + size, y)),
            new QrLineTo(new QrPoint(x + size, y + size)),
            new QrLineTo(new QrPoint(x, y + size)),
        ],
        IsClosed: true);

    [Fact]
    public void Empty_HasNoFigures()
    {
        Assert.True(QrPath.Empty.IsEmpty);
        Assert.Empty(QrPath.Empty.Figures);
    }

    [Fact]
    public void Builder_CollectsFiguresInOrder()
    {
        var path = new QrPathBuilder().Add(Square(0, 0, 1)).Add(Square(2, 0, 1)).Build(QrFillRule.NonZero);

        Assert.Equal(2, path.Figures.Count);
        Assert.Equal(new QrPoint(0, 0), path.Figures[0].Start);
        Assert.Equal(new QrPoint(2, 0), path.Figures[1].Start);
        Assert.Equal(QrFillRule.NonZero, path.FillRule);
        Assert.False(path.IsEmpty);
    }

    [Fact]
    public void Builder_CarriesTheFillRule() =>
        Assert.Equal(QrFillRule.EvenOdd, new QrPathBuilder().Build(QrFillRule.EvenOdd).FillRule);

    [Fact]
    public void Builder_AddRangeAppends()
    {
        var path = new QrPathBuilder().AddRange([Square(0, 0, 1), Square(1, 0, 1)]).Build(QrFillRule.NonZero);
        Assert.Equal(2, path.Figures.Count);
    }

    /// <summary>Segments compare by value, which lets tests assert a shape without reaching into it.</summary>
    [Fact]
    public void Segments_CompareByValue()
    {
        Assert.Equal(new QrLineTo(new QrPoint(1, 2)), new QrLineTo(new QrPoint(1, 2)));
        Assert.NotEqual<QrSegment>(new QrLineTo(new QrPoint(1, 2)), new QrLineTo(new QrPoint(1, 3)));
    }

    [Fact]
    public void Figures_KnowWhetherTheyClose()
    {
        Assert.True(Square(0, 0, 1).IsClosed);
        Assert.False(new QrFigure(new QrPoint(0, 0), [new QrLineTo(new QrPoint(1, 1))], IsClosed: false).IsClosed);
    }
}
