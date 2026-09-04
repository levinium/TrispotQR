using System.Windows;
using System.Windows.Media;
using TrispotQR.App.Rendering;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;

namespace TrispotQR.Tests;

public class QrGeometryBuilderTests
{
    private static QrMatrix Matrix() => QrEncoder.Encode("https://www.example.org", EccLevel.Medium).Matrix!;

    [Fact]
    public void Build_SizesTheDrawingInModuleUnitsIncludingTheQuietZone()
    {
        var matrix = Matrix();
        var style = QrStyle.Default with { QuietZoneModules = 4 };

        var drawing = QrGeometryBuilder.Build(matrix, style);

        Assert.Equal(matrix.Size + 8, drawing.SizeInUnits);
    }

    [Fact]
    public void Build_QuietZoneOfZero_LeavesNoMargin()
    {
        var matrix = Matrix();

        var drawing = QrGeometryBuilder.Build(matrix, QrStyle.Default with { QuietZoneModules = 0 });

        Assert.Equal(matrix.Size, drawing.SizeInUnits);
    }

    [Fact]
    public void Build_ProducesTheThreeExpectedLayers()
    {
        var drawing = QrGeometryBuilder.Build(Matrix(), QrStyle.Default);

        Assert.Contains(drawing.Layers, l => l.Name == QrLayerNames.Modules);
        Assert.Contains(drawing.Layers, l => l.Name == QrLayerNames.MarkerFrames);
        Assert.Contains(drawing.Layers, l => l.Name == QrLayerNames.MarkerCenters);
    }

    [Fact]
    public void Build_ModuleLayerExcludesTheFinderPatternAreas()
    {
        var matrix = Matrix();
        var style = QrStyle.Default with { QuietZoneModules = 4 };
        var drawing = QrGeometryBuilder.Build(matrix, style);
        var modules = AsGeometry(drawing.Layers.Single(l => l.Name == QrLayerNames.Modules));

        // Centre of each finder pattern, in drawing units.
        foreach (var (ox, oy) in matrix.FinderOrigins)
        {
            var point = new Point(4 + ox + 3.5, 4 + oy + 3.5);
            Assert.False(modules.FillContains(point), $"module layer should not cover the finder at {ox},{oy}");
        }
    }

    [Fact]
    public void Build_MarkerCenterLayerCoversTheMiddleOfEachFinder()
    {
        var matrix = Matrix();
        var drawing = QrGeometryBuilder.Build(matrix, QrStyle.Default with { QuietZoneModules = 4 });
        var centers = AsGeometry(drawing.Layers.Single(l => l.Name == QrLayerNames.MarkerCenters));

        foreach (var (ox, oy) in matrix.FinderOrigins)
        {
            Assert.True(centers.FillContains(new Point(4 + ox + 3.5, 4 + oy + 3.5)));
        }
    }

    [Fact]
    public void Build_MarkerFrameIsARingNotASolidBlock()
    {
        var matrix = Matrix();
        var drawing = QrGeometryBuilder.Build(matrix, QrStyle.Default with { QuietZoneModules = 4 });
        var frames = AsGeometry(drawing.Layers.Single(l => l.Name == QrLayerNames.MarkerFrames));

        // Dark on the outer ring, hollow in the light ring just inside it.
        Assert.True(frames.FillContains(new Point(4 + 0.5, 4 + 3.5)), "outer ring should be filled");
        Assert.False(frames.FillContains(new Point(4 + 1.5, 4 + 3.5)), "inner ring should be hollow");
        Assert.False(frames.FillContains(new Point(4 + 3.5, 4 + 3.5)), "core belongs to the centre layer");
    }

    [Fact]
    public void Build_ModuleScaleBelowOne_ShrinksEachModuleWithinItsCell()
    {
        var matrix = Matrix();
        var full = QrGeometryBuilder.Build(matrix, QrStyle.Default with { ModuleScale = 1.0, QuietZoneModules = 0 });
        var shrunk = QrGeometryBuilder.Build(matrix, QrStyle.Default with { ModuleScale = 0.6, QuietZoneModules = 0 });

        var fullArea = LayerArea(full, QrLayerNames.Modules);
        var shrunkArea = LayerArea(shrunk, QrLayerNames.Modules);

        Assert.True(shrunkArea < fullArea, $"expected {shrunkArea} < {fullArea}");
    }

    [Fact]
    public void Build_UsesForegroundForMarkersWhenNoMarkerColourIsSet()
    {
        var style = QrStyle.Default with
        {
            Foreground = WpfGeometryAdapter.ToRgbColor(Colors.DarkSlateBlue),
            MarkerFrameColor = null,
            MarkerCenterColor = null,
        };

        var drawing = QrGeometryBuilder.Build(Matrix(), style);

        Assert.Equal(WpfGeometryAdapter.ToRgbColor(Colors.DarkSlateBlue), LayerFill(drawing, QrLayerNames.MarkerFrames));
        Assert.Equal(WpfGeometryAdapter.ToRgbColor(Colors.DarkSlateBlue), LayerFill(drawing, QrLayerNames.MarkerCenters));
    }

    [Fact]
    public void Build_AppliesDistinctMarkerColoursWhenSet()
    {
        var style = QrStyle.Default with
        {
            Foreground = RgbColor.Black,
            MarkerFrameColor = WpfGeometryAdapter.ToRgbColor(Colors.Crimson),
            MarkerCenterColor = WpfGeometryAdapter.ToRgbColor(Colors.Goldenrod),
        };

        var drawing = QrGeometryBuilder.Build(Matrix(), style);

        Assert.Equal(RgbColor.Black, LayerFill(drawing, QrLayerNames.Modules));
        Assert.Equal(WpfGeometryAdapter.ToRgbColor(Colors.Crimson), LayerFill(drawing, QrLayerNames.MarkerFrames));
        Assert.Equal(WpfGeometryAdapter.ToRgbColor(Colors.Goldenrod), LayerFill(drawing, QrLayerNames.MarkerCenters));
    }

    [Fact]
    public void Build_TransparentBackground_LeavesTheBackgroundColourNull()
    {
        var drawing = QrGeometryBuilder.Build(Matrix(), QrStyle.Default with { Background = null });

        Assert.Null(drawing.Background);
    }

    [Fact]
    public void Build_OutlineDisabled_ProducesNoStroke()
    {
        var drawing = QrGeometryBuilder.Build(Matrix(), QrStyle.Default);

        Assert.All(drawing.Layers, l => Assert.Null(l.Stroke));
    }

    [Fact]
    public void Build_OutlineOnBoth_StrokesModulesAndMarkers()
    {
        var style = QrStyle.Default with
        {
            Outline = new OutlineStyle
            {
                Enabled = true,
                Color = RgbColor.White,
                ThicknessRatio = 0.1,
                Target = OutlineTarget.Both,
            },
        };

        var drawing = QrGeometryBuilder.Build(Matrix(), style);

        Assert.All(drawing.Layers, l => Assert.NotNull(l.Stroke));
        Assert.Equal(0.1, drawing.Layers[0].Stroke!.Thickness, 6);
    }

    [Fact]
    public void Build_OutlineOnMarkersOnly_LeavesTheModuleLayerUnstroked()
    {
        var style = QrStyle.Default with
        {
            Outline = new OutlineStyle
            {
                Enabled = true,
                Color = RgbColor.White,
                ThicknessRatio = 0.08,
                Target = OutlineTarget.Markers,
            },
        };

        var drawing = QrGeometryBuilder.Build(Matrix(), style);

        Assert.Null(drawing.Layers.Single(l => l.Name == QrLayerNames.Modules).Stroke);
        Assert.NotNull(drawing.Layers.Single(l => l.Name == QrLayerNames.MarkerFrames).Stroke);
    }

    [Theory]
    [InlineData(ModuleShape.Square)]
    [InlineData(ModuleShape.RoundedSquare)]
    [InlineData(ModuleShape.Circle)]
    [InlineData(ModuleShape.Diamond)]
    [InlineData(ModuleShape.Fluid)]
    public void Build_EveryModuleShape_ProducesNonEmptyGeometryInsideTheCanvas(ModuleShape shape)
    {
        var matrix = Matrix();
        var style = QrStyle.Default with { ModuleShape = shape, QuietZoneModules = 4 };

        var drawing = QrGeometryBuilder.Build(matrix, style);
        var bounds = AsGeometry(drawing.Layers.Single(l => l.Name == QrLayerNames.Modules)).Bounds;

        Assert.False(bounds.IsEmpty);
        Assert.True(bounds.Width > 0 && bounds.Height > 0);
        Assert.InRange(bounds.Left, 0, drawing.SizeInUnits);
        Assert.InRange(bounds.Top, 0, drawing.SizeInUnits);
        Assert.InRange(bounds.Right, 0, drawing.SizeInUnits);
        Assert.InRange(bounds.Bottom, 0, drawing.SizeInUnits);
    }

    [Theory]
    [InlineData(MarkerFrameShape.Square, MarkerCenterShape.Square)]
    [InlineData(MarkerFrameShape.RoundedSquare, MarkerCenterShape.RoundedSquare)]
    [InlineData(MarkerFrameShape.Circle, MarkerCenterShape.Circle)]
    [InlineData(MarkerFrameShape.Leaf, MarkerCenterShape.Square)]
    public void Build_EveryMarkerShape_KeepsTheFrameHollowAndTheCentreFilled(
        MarkerFrameShape frameShape, MarkerCenterShape centerShape)
    {
        var matrix = Matrix();
        var style = QrStyle.Default with
        {
            MarkerFrameShape = frameShape,
            MarkerCenterShape = centerShape,
            QuietZoneModules = 4,
        };

        var drawing = QrGeometryBuilder.Build(matrix, style);
        var frames = AsGeometry(drawing.Layers.Single(l => l.Name == QrLayerNames.MarkerFrames));
        var centers = AsGeometry(drawing.Layers.Single(l => l.Name == QrLayerNames.MarkerCenters));

        // The centre of a finder is always hollow in the frame layer and solid in the centre layer.
        Assert.False(frames.FillContains(new Point(4 + 3.5, 4 + 3.5)));
        Assert.True(centers.FillContains(new Point(4 + 3.5, 4 + 3.5)));
    }

    [Fact]
    public void Build_FluidShape_MergesAdjacentModulesIntoOneOutline()
    {
        // A fluid render of the same matrix draws fewer, larger connected figures than
        // the square render, which emits one rectangle per dark module.
        var matrix = Matrix();
        var square = QrGeometryBuilder.Build(matrix, QrStyle.Default with { ModuleShape = ModuleShape.Square });
        var fluid = QrGeometryBuilder.Build(matrix, QrStyle.Default with { ModuleShape = ModuleShape.Fluid });

        var squareArea = LayerArea(square, QrLayerNames.Modules);
        var fluidArea = LayerArea(fluid, QrLayerNames.Modules);

        // Fluid rounds only the outward-facing corners, so it covers slightly less than
        // solid squares but far more than disconnected dots would.
        Assert.True(fluidArea < squareArea);
        Assert.True(fluidArea > squareArea * 0.85, $"fluid {fluidArea} should stay close to square {squareArea}");
    }

    private static double LayerArea(QrDrawing drawing, string name) =>
        AsGeometry(drawing.Layers.Single(l => l.Name == name)).GetArea(0.001, ToleranceType.Absolute);

    /// <summary>
    /// The layer's path as WPF geometry, so these assertions keep asking exactly what they
    /// asked before the model became framework neutral. FillContains and GetArea have no
    /// equivalent on the model itself, and inventing one here would test the test.
    /// </summary>
    private static Geometry AsGeometry(QrLayer layer) => WpfGeometryAdapter.ToGeometry(layer.Path);

    private static RgbColor LayerFill(QrDrawing drawing, string name) =>
        drawing.Layers.Single(l => l.Name == name).Fill;
}
