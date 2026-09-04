using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrispotQR.Core.Export;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.Core.Validation;

namespace TrispotQR.Tests;

public class LogoTests : IDisposable
{
    private const string Payload = "https://www.example.org";

    private readonly string _squareLogo;
    private readonly string _wideLogo;

    public LogoTests()
    {
        _squareLogo = WriteTestImage(200, 200);
        _wideLogo = WriteTestImage(400, 100);
    }

    public void Dispose()
    {
        File.Delete(_squareLogo);
        File.Delete(_wideLogo);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void NoLogo_LeavesThePlacementNull()
    {
        var drawing = StaThread.Run(() => Build(QrStyle.Default));

        Assert.Null(drawing.Logo);
    }

    [Fact]
    public void Logo_IsCentredOnTheCode()
    {
        var (drawing, matrix) = StaThread.Run(() =>
        {
            var m = QrEncoder.Encode(Payload, EccLevel.High).Matrix!;
            return (QrGeometryBuilder.Build(m, LogoStyle(_squareLogo)), m);
        });

        var logo = drawing.Logo!;
        var centre = 4 + (matrix.Size / 2.0);

        Assert.Equal(centre, logo.X + (logo.Width / 2), 6);
        Assert.Equal(centre, logo.Y + (logo.Height / 2), 6);
    }

    [Fact]
    public void Logo_IsSizedToTheRequestedShareOfTheCode()
    {
        var (drawing, matrix) = StaThread.Run(() =>
        {
            var m = QrEncoder.Encode(Payload, EccLevel.High).Matrix!;
            return (QrGeometryBuilder.Build(m, LogoStyle(_squareLogo) with
            {
                Logo = new LogoStyle { Path = _squareLogo, SizeRatio = 0.25 },
            }), m);
        });

        Assert.Equal(matrix.Size * 0.25, drawing.Logo!.Width, 6);
    }

    [Fact]
    public void WideLogo_KeepsItsAspectRatioInsteadOfBeingSquashed()
    {
        var drawing = StaThread.Run(() => Build(LogoStyle(_wideLogo)));

        var logo = drawing.Logo!;
        Assert.Equal(4.0, logo.Width / logo.Height, 3);
    }

    [Fact]
    public void Logo_ClearsTheModulesUnderneathIt()
    {
        var (drawing, matrix) = StaThread.Run(() =>
        {
            var m = QrEncoder.Encode(Payload, EccLevel.High).Matrix!;
            return (QrGeometryBuilder.Build(m, LogoStyle(_squareLogo)), m);
        });

        var centre = new Point(4 + (matrix.Size / 2.0), 4 + (matrix.Size / 2.0));
        var modules = drawing.Layers.Single(l => l.Name == QrLayerNames.Modules).Geometry;

        Assert.False(modules.FillContains(centre), "modules should be cleared behind the logo");
    }

    [Fact]
    public void PunchShapeNone_LeavesTheModulesIntact()
    {
        var style = QrStyle.Default with
        {
            Ecc = EccLevel.High,
            Logo = new LogoStyle { Path = _squareLogo, SizeRatio = 0.22, PunchShape = LogoPunchShape.None },
        };

        var drawing = StaThread.Run(() => Build(style));

        Assert.NotNull(drawing.Logo);
        // The placement is still reported so the image gets drawn, but nothing was cut out.
        var modules = drawing.Layers.Single(l => l.Name == QrLayerNames.Modules).Geometry;
        Assert.False(modules.Bounds.IsEmpty);
    }

    [Fact]
    public void Logo_ReportsHowMuchOfTheCodeItCovers()
    {
        var drawing = StaThread.Run(() => Build(LogoStyle(_squareLogo)));

        // A 22% wide square plus padding covers appreciably more than 22% squared, but is
        // still a small share of the whole code.
        Assert.InRange(drawing.Logo!.CoverageRatio, 0.05, 0.20);
    }

    [Fact]
    public void LargerLogo_ReportsProportionallyMoreCoverage()
    {
        var (small, large) = StaThread.Run(() =>
        {
            var a = Build(QrStyle.Default with
            {
                Ecc = EccLevel.High,
                Logo = new LogoStyle { Path = _squareLogo, SizeRatio = 0.15, PunchPadding = 1.0 },
            });

            var b = Build(QrStyle.Default with
            {
                Ecc = EccLevel.High,
                Logo = new LogoStyle { Path = _squareLogo, SizeRatio = 0.40, PunchPadding = 1.0 },
            });

            return (a.Logo!.CoverageRatio, b.Logo!.CoverageRatio);
        });

        Assert.True(large > small * 1.5, $"expected {large:P1} to be well above {small:P1}");
        Assert.InRange(large, 0.15, 0.35);
    }

    [Fact]
    public void MissingLogoFile_DegradesToAPlainCodeRatherThanFailing()
    {
        var style = QrStyle.Default with
        {
            Logo = new LogoStyle { Path = @"C:\does\not\exist\logo.png" },
        };

        var drawing = StaThread.Run(() => Build(style));

        Assert.Null(drawing.Logo);
        Assert.False(drawing.Layers.Single(l => l.Name == QrLayerNames.Modules).Geometry.Bounds.IsEmpty);
    }

    [Theory]
    [InlineData(LogoPunchShape.Square)]
    [InlineData(LogoPunchShape.RoundedSquare)]
    [InlineData(LogoPunchShape.Circle)]
    public void CodeWithADefaultSizedLogo_StillScansAtHighErrorCorrection(LogoPunchShape punch)
    {
        var style = QrStyle.Default with
        {
            Ecc = EccLevel.High,
            Logo = new LogoStyle { Path = _squareLogo, SizeRatio = 0.22, PunchShape = punch },
        };

        var decoded = StaThread.Run(() =>
        {
            var drawing = Build(style);
            return QrDecoder.Decode(QrRenderer.RenderToBitmap(drawing, 700, Colors.White));
        });

        Assert.Equal(Payload, decoded);
    }

    [Theory]
    [InlineData(ModuleShape.Square)]
    [InlineData(ModuleShape.Circle)]
    [InlineData(ModuleShape.Fluid)]
    public void LogoWorksWithEveryModuleShape(ModuleShape shape)
    {
        var style = QrStyle.Default with
        {
            Ecc = EccLevel.High,
            ModuleShape = shape,
            Logo = new LogoStyle { Path = _squareLogo, SizeRatio = 0.22 },
        };

        var decoded = StaThread.Run(() =>
        {
            var drawing = Build(style);
            return QrDecoder.Decode(QrRenderer.RenderToBitmap(drawing, 700, Colors.White));
        });

        Assert.Equal(Payload, decoded);
    }

    [Fact]
    public void ScannabilityChecker_WarnsWhenTheLogoCoversTooMuch()
    {
        var style = QrStyle.Default with
        {
            Ecc = EccLevel.High,
            Logo = new LogoStyle { Path = _squareLogo, SizeRatio = 0.40, PunchPadding = 1.5 },
        };

        var result = StaThread.Run(() => ScannabilityChecker.Check(Payload, style));

        Assert.NotEqual(ScanVerdict.Good, result.Verdict);
        Assert.Contains("logo", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SvgExport_EmbedsTheLogoAsADataUriSoTheFileStaysSelfContained()
    {
        var svg = StaThread.Run(() => SvgExporter.ToSvg(Build(LogoStyle(_squareLogo)), 1024));

        Assert.Contains("<image", svg);
        Assert.Contains("data:image/png;base64,", svg);
        Assert.DoesNotContain(_squareLogo, svg);
    }

    private static QrStyle LogoStyle(string path) => QrStyle.Default with
    {
        Ecc = EccLevel.High,
        Logo = new LogoStyle { Path = path, SizeRatio = 0.22 },
    };

    private static QrDrawing Build(QrStyle style)
    {
        var matrix = QrEncoder.Encode(Payload, style.Ecc).Matrix!;
        return QrGeometryBuilder.Build(matrix, style);
    }

    /// <summary>Writes a solid test image so the suite carries no binary fixtures.</summary>
    private static string WriteTestImage(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"TrispotQR-logo-{Guid.NewGuid():N}.png");

        StaThread.Run(() =>
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.OrangeRed, null, new Rect(0, 0, width, height));
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            PngExporter.Save(bitmap, path);
            return true;
        });

        return path;
    }
}
