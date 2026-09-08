using TrispotQR.Core.Primitives;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;

namespace TrispotQR.Tests;

public class SkiaRasterizerTests
{
    private static QrDrawing Build(QrStyle? style = null)
    {
        var encoded = QrEncoder.Encode("https://example.org", EccLevel.Medium);
        return QrGeometryBuilder.Build(encoded.Matrix!, style ?? QrStyle.Default);
    }

    private static byte AlphaAt(RasterImage image, int x, int y) =>
        image.Pixels[(((y * image.Width) + x) * 4) + 3];

    [Fact]
    public void Render_ProducesTheRequestedSize()
    {
        var image = SkiaRasterizer.Render(Build(), 256);

        Assert.Equal(256, image.Width);
        Assert.Equal(256, image.Height);
        Assert.Equal(256 * 256 * 4, image.Pixels.Length);
    }

    [Fact]
    public void AWhiteBackground_IsOpaque() =>
        Assert.Equal(255, AlphaAt(SkiaRasterizer.Render(Build(QrStyle.Default with { Background = RgbColor.White }), 128), 2, 2));

    /// <summary>
    /// The quiet zone of a transparent code must have a genuinely empty alpha channel, not
    /// white pixels. The whole transparent-PNG feature rests on this.
    /// </summary>
    [Fact]
    public void NoBackground_LeavesTheQuietZoneTransparent() =>
        Assert.Equal(0, AlphaAt(SkiaRasterizer.Render(Build(QrStyle.Default with { Background = null }), 128), 2, 2));

    [Fact]
    public void BackgroundOverride_FlattensATransparentCode() =>
        Assert.Equal(255, AlphaAt(
            SkiaRasterizer.Render(Build(QrStyle.Default with { Background = null }), 128, RgbColor.White), 2, 2));

    [Fact]
    public void EncodePng_ProducesARealPngHeader() =>
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], SkiaRasterizer.EncodePng(SkiaRasterizer.Render(Build(), 64)).Take(4));

    [Fact]
    public void Render_RejectsANonPositiveSize() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SkiaRasterizer.Render(Build(), 0));

    /// <summary>The code itself must actually be painted, not just the background.</summary>
    [Fact]
    public void Render_PaintsDarkModules()
    {
        var image = SkiaRasterizer.Render(Build(QrStyle.Default with { Background = RgbColor.White }), 256);
        var dark = 0;

        for (var i = 0; i < image.Pixels.Length; i += 4)
        {
            if (image.Pixels[i] < 64)
            {
                dark++;
            }
        }

        Assert.True(dark > 1000, $"only {dark} dark pixels, the code was not drawn");
    }
}
