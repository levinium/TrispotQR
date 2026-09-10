using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.Core.Validation;
using TrispotQR.UI.Rendering;

namespace TrispotQR.UI.Tests;

public class AvaloniaGeometryTests
{
    // [AvaloniaFact], not [Fact]: StreamGeometry.Open() resolves Avalonia's platform render
    // interface, which only exists once the headless platform has been set up. Only
    // [AvaloniaFact] does that setup; a plain [Fact] hits "Unable to locate
    // IPlatformRenderInterface" before the geometry logic under test ever runs.
    [AvaloniaFact]
    public void AnEmptyPathBecomesAnEmptyGeometry()
    {
        var geometry = AvaloniaGeometry.ToStreamGeometry(QrPath.Empty);

        Assert.Equal(0, geometry.Bounds.Width);
        Assert.Equal(0, geometry.Bounds.Height);
    }

    [AvaloniaFact]
    public void ARectangularFigureKeepsItsBounds()
    {
        var rectangle = new QrPath(
            [
                new QrFigure(
                    new QrPoint(1, 1),
                    [
                        new QrLineTo(new QrPoint(3, 1)),
                        new QrLineTo(new QrPoint(3, 4)),
                        new QrLineTo(new QrPoint(1, 4)),
                    ],
                    IsClosed: true),
            ],
            QrFillRule.NonZero);

        var geometry = AvaloniaGeometry.ToStreamGeometry(rectangle);

        Assert.Equal(1, geometry.Bounds.X, 3);
        Assert.Equal(1, geometry.Bounds.Y, 3);
        Assert.Equal(2, geometry.Bounds.Width, 3);
        Assert.Equal(3, geometry.Bounds.Height, 3);
    }

    [AvaloniaFact]
    public void TheFillRuleCarriesOver()
    {
        // A ring: an outer square with an inner square inside it, both wound the same way.
        // Under EvenOdd the middle is a hole. Getting this wrong is how a marker centre
        // ends up a solid block, so it is asserted rather than assumed.
        var ring = new QrPath(
            [
                new QrFigure(
                    new QrPoint(0, 0),
                    [new QrLineTo(new QrPoint(6, 0)), new QrLineTo(new QrPoint(6, 6)), new QrLineTo(new QrPoint(0, 6))],
                    IsClosed: true),
                new QrFigure(
                    new QrPoint(2, 2),
                    [new QrLineTo(new QrPoint(4, 2)), new QrLineTo(new QrPoint(4, 4)), new QrLineTo(new QrPoint(2, 4))],
                    IsClosed: true),
            ],
            QrFillRule.EvenOdd);

        var geometry = AvaloniaGeometry.ToStreamGeometry(ring);

        Assert.False(geometry.FillContains(new Point(3, 3)));
        Assert.True(geometry.FillContains(new Point(1, 1)));
    }
}

public class SkiaCoexistenceTests
{
    [AvaloniaFact]
    public void AvaloniaRendersWhileCoreRasterisesAndDecodes()
    {
        // Avalonia 12.1.2 is compiled against SkiaSharp 3.119.4; Core pins 4.151.2 and NuGet
        // unifies the graph up to it. That was proven by hand on Windows. This test exists so
        // it is also proven on Linux and macOS, where the native libSkiaSharp has to load as
        // well, and so it stays proven whenever either version moves.
        const string payload = "https://www.example.org";

        var encoded = QrEncoder.Encode(payload, EccLevel.Medium);
        Assert.True(encoded.Success);

        var drawing = QrGeometryBuilder.Build(encoded.Matrix!, StylePresets.BuiltIn[0].Style);
        var raster = SkiaRasterizer.Render(drawing, 512);

        Assert.Equal(payload, QrDecoder.Decode(raster));

        var window = new Window { Width = 300, Height = 300 };
        window.Content = new Avalonia.Controls.Shapes.Path
        {
            Data = AvaloniaGeometry.ToStreamGeometry(drawing.Layers[0].Path),
            Fill = Brushes.Black,
        };
        window.Show();

        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.Equal(new PixelSize(300, 300), frame!.PixelSize);
    }
}
