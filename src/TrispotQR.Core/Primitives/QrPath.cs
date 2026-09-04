namespace TrispotQR.Core.Primitives;

/// <summary>A point in module units.</summary>
public readonly record struct QrPoint(double X, double Y);

/// <summary>How overlapping parts of a path are filled.</summary>
public enum QrFillRule
{
    NonZero,
    EvenOdd,
}

/// <summary>One step along a figure, starting wherever the previous step ended.</summary>
public abstract record QrSegment;

/// <summary>A straight line.</summary>
public sealed record QrLineTo(QrPoint To) : QrSegment;

/// <summary>
/// A circular arc. Always the small arc: every corner this app draws is a quarter circle
/// or less, so there is no large-arc flag to carry around.
/// </summary>
public sealed record QrArcTo(QrPoint To, double Radius, bool Clockwise) : QrSegment;

/// <summary>A cubic bezier. Not produced by ShapeFactory; needed because Skia emits them.</summary>
public sealed record QrCubicTo(QrPoint C1, QrPoint C2, QrPoint To) : QrSegment;

/// <summary>One continuous outline.</summary>
public sealed record QrFigure(QrPoint Start, IReadOnlyList<QrSegment> Segments, bool IsClosed);

/// <summary>
/// The geometry model everything is drawn from, in module units.
///
/// This is the single source the preview, the PNG rasteriser and the SVG writer all derive
/// from, so none of them can drift from the others. It deliberately owes nothing to any UI
/// framework: that is what lets Core target plain .NET and run on macOS, Linux and mobile.
/// </summary>
public sealed record QrPath(IReadOnlyList<QrFigure> Figures, QrFillRule FillRule)
{
    public static readonly QrPath Empty = new([], QrFillRule.NonZero);

    public bool IsEmpty => Figures.Count == 0;
}
