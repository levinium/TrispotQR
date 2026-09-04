using System.IO;
using System.Windows.Media;
using System.Xml.Linq;
using TrispotQR.Core.Export;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.Core.Validation;

namespace TrispotQR.Tests;

public class SvgExporterTests
{
    private const string Payload = "https://www.example.org";
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    [Fact]
    public void ToSvg_ProducesWellFormedSvgWithTheRightDimensions()
    {
        var document = XDocument.Parse(Export(QrStyle.Default, 1024));
        var root = document.Root!;

        Assert.Equal(Svg + "svg", root.Name);
        Assert.Equal("1024", root.Attribute("width")!.Value);
        Assert.Equal("1024", root.Attribute("height")!.Value);

        // The viewBox is in module units, which is what makes the file resolution free.
        var viewBox = root.Attribute("viewBox")!.Value.Split(' ');
        Assert.Equal("0", viewBox[0]);
        Assert.Equal("0", viewBox[1]);
        Assert.Equal(viewBox[2], viewBox[3]);
    }

    [Fact]
    public void ToSvg_EmitsOnePathPerLayer()
    {
        var document = XDocument.Parse(Export(QrStyle.Default, 512));

        var paths = document.Root!.Elements(Svg + "path").ToList();
        Assert.Equal(3, paths.Count);
    }

    [Fact]
    public void ToSvg_StripsTheWpfFillRulePrefixAndTranslatesIt()
    {
        var document = XDocument.Parse(Export(QrStyle.Default, 512));

        foreach (var path in document.Root!.Elements(Svg + "path"))
        {
            var data = path.Attribute("d")!.Value;

            // WPF prefixes its path mini-language with F0 or F1. Left in place, an SVG
            // renderer either drops the path or draws it wrong.
            Assert.DoesNotContain("F0", data);
            Assert.DoesNotContain("F1", data);
            Assert.StartsWith("M", data.TrimStart());

            var fillRule = path.Attribute("fill-rule")?.Value;
            Assert.True(fillRule is null or "evenodd" or "nonzero", $"unexpected fill-rule: {fillRule}");
        }
    }

    [Fact]
    public void ToSvg_MarkerFrameLayerUsesEvenOddSoTheRingStaysHollow()
    {
        var document = XDocument.Parse(Export(QrStyle.Default, 512));
        var frames = document.Root!.Elements(Svg + "path")
            .Single(p => p.Attribute("id")?.Value == QrLayerNames.MarkerFrames);

        Assert.Equal("evenodd", frames.Attribute("fill-rule")!.Value);
    }

    [Fact]
    public void ToSvg_UsesOnlyPathCommandsSvgUnderstands()
    {
        var document = XDocument.Parse(Export(QrStyle.Default with { ModuleShape = ModuleShape.Circle }, 512));

        foreach (var path in document.Root!.Elements(Svg + "path"))
        {
            var letters = path.Attribute("d")!.Value.Where(char.IsLetter).Distinct();
            Assert.All(letters, c => Assert.Contains(char.ToUpperInvariant(c), "MLHVCSQTAZ"));
        }
    }

    [Fact]
    public void ToSvg_WhiteBackground_EmitsABackgroundRect()
    {
        var document = XDocument.Parse(Export(QrStyle.Default with { Background = Colors.White }, 512));

        var rect = document.Root!.Element(Svg + "rect");
        Assert.NotNull(rect);
        Assert.Equal("#FFFFFF", rect!.Attribute("fill")!.Value);
    }

    [Fact]
    public void ToSvg_TransparentBackground_EmitsNoBackgroundRect()
    {
        var document = XDocument.Parse(Export(QrStyle.Default with { Background = null }, 512));

        Assert.Null(document.Root!.Element(Svg + "rect"));
    }

    [Fact]
    public void ToSvg_CarriesTheDistinctMarkerColours()
    {
        var style = QrStyle.Default with
        {
            Foreground = Color.FromRgb(0x1B, 0x2A, 0x4A),
            MarkerFrameColor = Color.FromRgb(0x8A, 0x6D, 0x3B),
            MarkerCenterColor = Color.FromRgb(0x1B, 0x2A, 0x4A),
        };

        var document = XDocument.Parse(Export(style, 512));
        var byId = document.Root!.Elements(Svg + "path").ToDictionary(p => p.Attribute("id")!.Value);

        Assert.Equal("#1B2A4A", byId[QrLayerNames.Modules].Attribute("fill")!.Value);
        Assert.Equal("#8A6D3B", byId[QrLayerNames.MarkerFrames].Attribute("fill")!.Value);
        Assert.Equal("#1B2A4A", byId[QrLayerNames.MarkerCenters].Attribute("fill")!.Value);
    }

    [Fact]
    public void ToSvg_OutlineEnabled_EmitsStrokeAttributes()
    {
        var style = QrStyle.Default with
        {
            Outline = new OutlineStyle
            {
                Enabled = true,
                Color = Colors.White,
                ThicknessRatio = 0.08,
                Target = OutlineTarget.Both,
            },
        };

        var modules = XDocument.Parse(Export(style, 512)).Root!
            .Elements(Svg + "path")
            .Single(p => p.Attribute("id")!.Value == QrLayerNames.Modules);

        Assert.Equal("#FFFFFF", modules.Attribute("stroke")!.Value);
        Assert.Equal("0.08", modules.Attribute("stroke-width")!.Value);
    }

    [Theory]
    [InlineData(ModuleShape.Square)]
    [InlineData(ModuleShape.RoundedSquare)]
    [InlineData(ModuleShape.Circle)]
    [InlineData(ModuleShape.Diamond)]
    [InlineData(ModuleShape.Fluid)]
    public void ToSvg_PathDataReparsesIntoAScannableCode(ModuleShape shape)
    {
        // Re-reading the emitted path data proves the geometry survived serialisation,
        // and decoding the re-rendered result proves the SVG is not just well formed but
        // still a working QR code.
        var svg = Export(QrStyle.Default with { ModuleShape = shape }, 512);
        var document = XDocument.Parse(svg);
        var viewBox = document.Root!.Attribute("viewBox")!.Value.Split(' ');
        var units = double.Parse(viewBox[2], System.Globalization.CultureInfo.InvariantCulture);

        var decoded = StaThread.Run(() =>
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                var scale = 512 / units;
                context.PushTransform(new ScaleTransform(scale, scale));
                context.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, units, units));

                foreach (var path in document.Root!.Elements(Svg + "path"))
                {
                    var geometry = Geometry.Parse(path.Attribute("d")!.Value);
                    geometry.FillRule(path.Attribute("fill-rule")?.Value);
                    context.DrawGeometry(Brushes.Black, null, geometry);
                }

                context.Pop();
            }

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(512, 512, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return QrDecoder.Decode(bitmap);
        });

        Assert.Equal(Payload, decoded);
    }

    [Fact]
    public void Save_WritesAFileThatParsesAsXml()
    {
        var path = Path.Combine(Path.GetTempPath(), $"TrispotQR-{Guid.NewGuid():N}.svg");

        try
        {
            var drawing = BuildDrawing(QrStyle.Default);
            SvgExporter.Save(drawing, 1024, path);

            Assert.True(File.Exists(path));
            var document = XDocument.Load(path);
            Assert.Equal(Svg + "svg", document.Root!.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Export(QrStyle style, int pixelSize) => SvgExporter.ToSvg(BuildDrawing(style), pixelSize);

    private static QrDrawing BuildDrawing(QrStyle style)
    {
        var matrix = QrEncoder.Encode(Payload, style.Ecc).Matrix!;
        return QrGeometryBuilder.Build(matrix, style);
    }
}

internal static class GeometryTestExtensions
{
    /// <summary>Applies an SVG fill-rule string to a parsed geometry, when it is a path.</summary>
    public static void FillRule(this Geometry geometry, string? rule)
    {
        if (geometry is PathGeometry path)
        {
            path.FillRule = rule == "evenodd" ? System.Windows.Media.FillRule.EvenOdd : System.Windows.Media.FillRule.Nonzero;
        }
    }
}
