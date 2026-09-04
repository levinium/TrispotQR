using TrispotQR.Core.Primitives;
using TrispotQR.Core.Rendering;

namespace TrispotQR.Tests;

public class SkiaPathOpsTests
{
    private static QrPath Square(double x, double y, double size) =>
        new QrPathBuilder().Add(ShapeFactory.RoundedRect(x, y, size, 0)).Build(QrFillRule.NonZero);

    /// <summary>
    /// The logo punch-out. A hole in the middle of a shape is the whole point, and it has
    /// to come back as a QrPath so the SVG export shows the same hole the PNG does.
    /// </summary>
    [Fact]
    public void Exclude_CutsAHoleAndKeepsTheOutline()
    {
        var result = SkiaPathOps.Exclude(Square(0, 0, 10), Square(4, 4, 2));

        using var sk = SkiaPath.ToSKPath(result);
        Assert.Equal(0f, sk.Bounds.Left, 3);
        Assert.Equal(10f, sk.Bounds.Right, 3);
        Assert.False(sk.Contains(5f, 5f));
        Assert.True(sk.Contains(1f, 1f));
    }

    [Fact]
    public void Exclude_LeavesTheSubjectAloneWhenNothingOverlaps()
    {
        var result = SkiaPathOps.Exclude(Square(0, 0, 2), Square(50, 50, 2));

        using var sk = SkiaPath.ToSKPath(result);
        Assert.True(sk.Contains(1f, 1f));
        Assert.Equal(2f, sk.Bounds.Right, 3);
    }

    [Fact]
    public void Exclude_OfAnEmptySubjectIsEmpty() =>
        Assert.True(SkiaPathOps.Exclude(QrPath.Empty, Square(0, 0, 1)).IsEmpty);
}
