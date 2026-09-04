using TrispotQR.Core.Export;
using TrispotQR.Core.Primitives;

namespace TrispotQR.Tests;

public class SvgPathDataTests
{
    private static QrPath Of(QrFigure figure) => new QrPathBuilder().Add(figure).Build(QrFillRule.NonZero);

    [Fact]
    public void AClosedSquare_IsMoveLinesAndZ()
    {
        var figure = new QrFigure(
            new QrPoint(0, 0),
            [
                new QrLineTo(new QrPoint(2, 0)),
                new QrLineTo(new QrPoint(2, 2)),
                new QrLineTo(new QrPoint(0, 2)),
            ],
            IsClosed: true);

        Assert.Equal("M0,0 L2,0 L2,2 L0,2 Z", SvgPathData.ToData(Of(figure)));
    }

    [Fact]
    public void AnArc_UsesTheSvgArcCommand()
    {
        var figure = new QrFigure(
            new QrPoint(0, 2),
            [new QrArcTo(new QrPoint(2, 0), 2, Clockwise: true)],
            IsClosed: false);

        // rx,ry rotation large-arc sweep x,y
        Assert.Equal("M0,2 A2,2 0 0 1 2,0", SvgPathData.ToData(Of(figure)));
    }

    [Fact]
    public void ACounterClockwiseArc_FlipsTheSweepFlag()
    {
        var figure = new QrFigure(
            new QrPoint(0, 2),
            [new QrArcTo(new QrPoint(2, 0), 2, Clockwise: false)],
            IsClosed: false);

        Assert.Contains(" 0 0 0 ", SvgPathData.ToData(Of(figure)));
    }

    [Fact]
    public void ACubic_UsesTheCurveCommand()
    {
        var figure = new QrFigure(
            new QrPoint(0, 0),
            [new QrCubicTo(new QrPoint(1, 0), new QrPoint(2, 1), new QrPoint(2, 2))],
            IsClosed: false);

        Assert.Equal("M0,0 C1,0 2,1 2,2", SvgPathData.ToData(Of(figure)));
    }

    [Fact]
    public void Numbers_AreInvariantAndTrimmed()
    {
        var figure = new QrFigure(
            new QrPoint(1.5, 2.25), [new QrLineTo(new QrPoint(3.0, 4.123456))], IsClosed: false);

        Assert.Equal("M1.5,2.25 L3,4.1235", SvgPathData.ToData(Of(figure)));
    }

    [Fact]
    public void AnEmptyPath_ProducesNothing() => Assert.Equal(string.Empty, SvgPathData.ToData(QrPath.Empty));

    [Fact]
    public void MultipleFigures_EachStartWithAMove()
    {
        var one = new QrFigure(new QrPoint(0, 0), [new QrLineTo(new QrPoint(1, 0))], IsClosed: true);
        var two = new QrFigure(new QrPoint(5, 5), [new QrLineTo(new QrPoint(6, 5))], IsClosed: true);
        var data = SvgPathData.ToData(new QrPathBuilder().Add(one).Add(two).Build(QrFillRule.NonZero));

        Assert.Equal("M0,0 L1,0 Z M5,5 L6,5 Z", data);
    }

    [Fact]
    public void FillRule_MapsToTheSvgKeywords()
    {
        Assert.Equal("nonzero", SvgPathData.FillRule(QrFillRule.NonZero));
        Assert.Equal("evenodd", SvgPathData.FillRule(QrFillRule.EvenOdd));
    }
}
