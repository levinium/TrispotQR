namespace TrispotQR.Core.Qr;

/// <summary>
/// An immutable grid of QR modules plus the metadata the renderer and the UI need.
/// Coordinates are module based with (0,0) at the top left. The quiet zone is not
/// part of the matrix; the renderer adds it.
/// </summary>
public sealed class QrMatrix
{
    private readonly bool[] _modules;

    internal QrMatrix(int size, int version, EccLevel effectiveEcc, bool[] modules)
    {
        Size = size;
        Version = version;
        EffectiveEcc = effectiveEcc;
        _modules = modules;
    }

    /// <summary>Width and height in modules. Always 21 + 4 * (version - 1).</summary>
    public int Size { get; }

    /// <summary>QR version, 1 through 40.</summary>
    public int Version { get; }

    /// <summary>
    /// The error correction level actually used, which may be stronger than the level
    /// requested when the content left slack in the symbol.
    /// </summary>
    public EccLevel EffectiveEcc { get; }

    /// <summary>
    /// True when the module at the given coordinate is dark. Coordinates outside the
    /// matrix return false, so neighbour lookups in the renderer need no bounds guards.
    /// </summary>
    public bool IsDark(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size)
        {
            return false;
        }

        return _modules[(y * Size) + x];
    }

    /// <summary>
    /// True when the coordinate falls inside one of the three 7x7 finder patterns.
    /// The renderer draws those separately so they can carry their own shape and colour.
    /// </summary>
    public bool IsFinderPattern(int x, int y)
    {
        var last = Size - 7;
        return (x < 7 && y < 7)
            || (x >= last && y < 7)
            || (x < 7 && y >= last);
    }

    /// <summary>The top-left corner of each of the three finder patterns.</summary>
    public IReadOnlyList<(int X, int Y)> FinderOrigins =>
        new[] { (0, 0), (Size - 7, 0), (0, Size - 7) };
}
