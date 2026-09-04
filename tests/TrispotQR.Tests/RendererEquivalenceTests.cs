using System.Windows.Media.Imaging;
using TrispotQR.App.Rendering;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.Core.Validation;

namespace TrispotQR.Tests;

/// <summary>
/// The Skia renderer must produce codes that read back the same as the WPF renderer's.
///
/// This is the guard for the whole port. Pixel-identical output is explicitly not the goal,
/// because two engines antialias differently and it would fail for reasons nobody should
/// care about. What matters is that a code rendered the new way still scans and still
/// carries the same content, for every style the app can produce.
///
/// Deleted in Phase 2 along with the WPF renderer it compares against.
/// </summary>
[Collection("UI")]
public class RendererEquivalenceTests
{
    private const string Payload = "https://example.org/equivalence";

    private readonly WpfHost _host;

    public RendererEquivalenceTests(WpfHost host) => _host = host;

    public static TheoryData<string> Presets
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var preset in StylePresets.BuiltIn)
            {
                data.Add(preset.Name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void BothRenderers_ReadBackTheSameContent(string presetName)
    {
        var style = StylePresets.BuiltIn.Single(p => p.Name == presetName).Style;
        var encoded = QrEncoder.Encode(Payload, style.Ecc);
        var drawing = QrGeometryBuilder.Build(encoded.Matrix!, style);

        var skia = QrDecoder.Decode(SkiaRasterizer.Render(drawing, 512, RgbColor.White));
        var wpf = _host.Run(() => DecodeWpf(WpfQrRenderer.RenderToBitmap(drawing, 512, RgbColor.White)));

        Assert.Equal(Payload, wpf);
        Assert.Equal(Payload, skia);
    }

    /// <summary>
    /// Every shape combination, not just the presets. The same guard StyleMatrixScanTests
    /// applies, pointed at whether the renderer swap changed anything.
    /// </summary>
    [Fact]
    public void EveryShapeCombination_StillDecodesUnderSkia()
    {
        var failures = new List<string>();

        foreach (var module in Enum.GetValues<ModuleShape>())
        {
            foreach (var frame in Enum.GetValues<MarkerFrameShape>())
            {
                foreach (var centre in Enum.GetValues<MarkerCenterShape>())
                {
                    var style = QrStyle.Default with
                    {
                        ModuleShape = module,
                        MarkerFrameShape = frame,
                        MarkerCenterShape = centre,
                        Background = RgbColor.White,
                    };

                    var encoded = QrEncoder.Encode(Payload, style.Ecc);
                    var drawing = QrGeometryBuilder.Build(encoded.Matrix!, style);
                    var read = QrDecoder.Decode(SkiaRasterizer.Render(drawing, 512));

                    if (read != Payload)
                    {
                        failures.Add($"{module}/{frame}/{centre} read back as {read ?? "nothing"}");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>Pbgra32 is premultiplied BGRA, which is exactly what RasterImage carries.</summary>
    private static string? DecodeWpf(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        return QrDecoder.Decode(new RasterImage(bitmap.PixelWidth, bitmap.PixelHeight, pixels));
    }
}
