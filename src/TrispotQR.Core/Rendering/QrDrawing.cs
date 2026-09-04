using TrispotQR.Core.Primitives;

namespace TrispotQR.Core.Rendering;

/// <summary>Stable identifiers for the drawing layers, used by exporters and tests.</summary>
public static class QrLayerNames
{
    public const string Modules = "modules";
    public const string MarkerFrames = "marker-frames";
    public const string MarkerCenters = "marker-centers";
}

/// <summary>An outline drawn around a layer. Thickness is in module units.</summary>
public sealed record QrStroke(RgbColor Color, double Thickness);

/// <summary>
/// One painted layer of the code: a path plus how to fill and stroke it. Layers exist so
/// the data modules and the two parts of the corner markers can carry independent shapes
/// and colours while still being described by a single flat drawing.
/// </summary>
public sealed class QrLayer
{
    public QrLayer(string name, QrPath path, RgbColor fill, QrStroke? stroke)
    {
        Name = name;
        Path = path;
        Fill = fill;
        Stroke = stroke;
    }

    public string Name { get; }

    /// <summary>The layer's geometry, in module units.</summary>
    public QrPath Path { get; }

    public RgbColor Fill { get; }

    public QrStroke? Stroke { get; }
}

/// <summary>Where and how large the centre logo sits, in module units.</summary>
public sealed record LogoPlacement(string Path, double X, double Y, double Width, double Height)
{
    /// <summary>Share of the code area the logo covers, used for the scannability warning.</summary>
    public double CoverageRatio { get; init; }
}

/// <summary>
/// A complete, resolution independent description of a rendered code. Coordinates are in
/// module units with the quiet zone included, so the same drawing scales to a 96 px
/// preview thumbnail and a 4096 px print export with no second code path.
/// </summary>
public sealed class QrDrawing
{
    public QrDrawing(double sizeInUnits, RgbColor? background, IReadOnlyList<QrLayer> layers, LogoPlacement? logo)
    {
        SizeInUnits = sizeInUnits;
        Background = background;
        Layers = layers;
        Logo = logo;
    }

    /// <summary>Width and height of the whole canvas in module units, quiet zone included.</summary>
    public double SizeInUnits { get; }

    /// <summary>Background colour, or null for transparent.</summary>
    public RgbColor? Background { get; }

    /// <summary>Painted back to front: modules, then marker frames, then marker centres.</summary>
    public IReadOnlyList<QrLayer> Layers { get; }

    public LogoPlacement? Logo { get; }
}
