using System.Windows;
using System.Windows.Media;
using TrispotQR.App.Rendering;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.Core.Validation;

namespace TrispotQR.Tests;

/// <summary>
/// The badge has to be trustworthy in both directions. These cover the direction that is
/// easy to miss: plainly good codes must never be reported as broken.
///
/// This started as a real defect. The checker used only ZXing's camera-like pass, which
/// hunts for the corner markers, and that pass wrongly failed 15 of 300 plain black
/// on white codes. A user would have been told a perfectly ordinary QR code did not scan.
/// </summary>
public class ScannabilityFalseAlarmTests
{
    /// <summary>
    /// Payloads chosen to sweep across QR versions and lengths rather than to be
    /// interesting in themselves. The original failures clustered on version 3.
    /// </summary>
    public static TheoryData<string> Payloads()
    {
        var data = new TheoryData<string>();

        for (var i = 1; i <= 60; i++)
        {
            data.Add($"https://www.example.org/copy-{i}");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Payloads))]
    public void PlainCode_IsNeverReportedAsUnscannable(string payload)
    {
        var result = StaThread.Run(() => ScannabilityChecker.Check(payload, QrStyle.Default));

        Assert.True(
            result.Verdict == ScanVerdict.Good,
            $"a plain black on white code was reported as {result.Verdict}: {result.Message}");
    }

    [Theory]
    [InlineData(ModuleShape.Square)]
    [InlineData(ModuleShape.RoundedSquare)]
    [InlineData(ModuleShape.Circle)]
    [InlineData(ModuleShape.Diamond)]
    [InlineData(ModuleShape.Fluid)]
    public void EveryBuiltInShape_IsReportedGoodAcrossManyPayloads(ModuleShape shape)
    {
        var complaints = StaThread.Run(() =>
        {
            var found = new List<string>();

            for (var i = 1; i <= 20; i++)
            {
                var payload = $"https://www.example.org/shape-{i}";
                var result = ScannabilityChecker.Check(payload, QrStyle.Default with { ModuleShape = shape });

                if (result.Verdict != ScanVerdict.Good)
                {
                    found.Add($"{payload}: {result.Verdict} ({result.Message})");
                }
            }

            return found;
        });

        Assert.True(complaints.Count == 0, string.Join("\n", complaints));
    }

    [Fact]
    public void EveryBuiltInPreset_IsReportedGood()
    {
        var complaints = StaThread.Run(() =>
            StylePresets.BuiltIn
                .Select(preset => new { preset.Name, Result = ScannabilityChecker.Check("https://www.example.org", preset.Style) })
                .Where(x => x.Result.Verdict != ScanVerdict.Good)
                .Select(x => $"{x.Name}: {x.Result.Verdict} ({x.Result.Message})")
                .ToList());

        Assert.True(complaints.Count == 0, string.Join("\n", complaints));
    }

    /// <summary>
    /// The other direction. Being forgiving about a clean render must not turn into
    /// accepting a code that genuinely cannot be read.
    /// </summary>
    [Fact]
    public void ACodeWithItsCornerMarkersPaintedOut_IsStillRejected()
    {
        const string payload = "https://www.example.org/damaged";

        var decoded = StaThread.Run(() =>
        {
            var matrix = QrEncoder.Encode(payload, EccLevel.Medium).Matrix!;
            var drawing = QrGeometryBuilder.Build(matrix, QrStyle.Default);

            var visual = new DrawingVisual();
            var scale = 512 / drawing.SizeInUnits;

            using (var context = visual.RenderOpen())
            {
                context.PushTransform(new ScaleTransform(scale, scale));
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, drawing.SizeInUnits, drawing.SizeInUnits));

                foreach (var layer in drawing.Layers)
                {
                    context.DrawGeometry(
                        WpfGeometryAdapter.ToBrush(layer.Fill),
                        WpfGeometryAdapter.ToPen(layer.Stroke),
                        WpfGeometryAdapter.ToGeometry(layer.Path));
                }

                foreach (var (ox, oy) in matrix.FinderOrigins)
                {
                    context.DrawRectangle(Brushes.White, null, new Rect(ox + 4, oy + 4, 7, 7));
                }

                context.Pop();
            }

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(512, 512, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            return QrDecoder.Decode(ToRasterImage(bitmap));
        });

        Assert.Null(decoded);
    }

    /// <summary>WPF's Pbgra32 is premultiplied BGRA, exactly what RasterImage carries.</summary>
    private static RasterImage ToRasterImage(System.Windows.Media.Imaging.BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return new RasterImage(bitmap.PixelWidth, bitmap.PixelHeight, pixels);
    }
}
