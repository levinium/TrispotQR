namespace TrispotQR.Core.Primitives;

/// <summary>Accumulates figures, then hands back an immutable <see cref="QrPath"/>.</summary>
public sealed class QrPathBuilder
{
    private readonly List<QrFigure> _figures = [];

    public QrPathBuilder Add(QrFigure figure)
    {
        _figures.Add(figure);
        return this;
    }

    public QrPathBuilder AddRange(IEnumerable<QrFigure> figures)
    {
        _figures.AddRange(figures);
        return this;
    }

    public QrPath Build(QrFillRule fillRule) => new([.. _figures], fillRule);
}
