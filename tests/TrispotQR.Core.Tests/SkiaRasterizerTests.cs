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

    [Fact]
    public void FlattenOnto_LeavesNothingTransparent()
    {
        // The point of flattening: whatever comes out has no alpha left to interpret. Word and
        // Outlook reach for the plain bitmap on the clipboard and, handed one with transparency,
        // paste a black box.
        var flattened = SkiaRasterizer.FlattenOnto(
            SkiaRasterizer.Render(Build(QrStyle.Default with { Background = null }), 64),
            RgbColor.White);

        for (var i = 3; i < flattened.Pixels.Length; i += 4)
        {
            Assert.Equal(255, flattened.Pixels[i]);
        }
    }

    [Fact]
    public void FlattenOnto_ShowsTheBackgroundWhereTheImageWasTransparent()
    {
        // A code with no background of its own, flattened onto red: the quiet zone is where the
        // image was empty, so its corner pixel has to come out as the colour underneath.
        var flattened = SkiaRasterizer.FlattenOnto(
            SkiaRasterizer.Render(Build(QrStyle.Default with { Background = null }), 64),
            RgbColor.FromRgb(0xFF, 0x00, 0x00));

        // BGRA, so the corner reads blue, green, red, alpha.
        Assert.Equal(0x00, flattened.Pixels[0]);
        Assert.Equal(0x00, flattened.Pixels[1]);
        Assert.Equal(0xFF, flattened.Pixels[2]);
        Assert.Equal(255, flattened.Pixels[3]);
    }

    [Fact]
    public void FlattenOnto_BlendsAHalfTransparentPixelAsPremultiplied()
    {
        // The case the other three miss, and the reason this one is built by hand rather than
        // rendered: premultiplied and straight alpha agree exactly when alpha is 0 or 255, so a
        // test that only sees the quiet zone and a drawn module cannot tell the two apart. It
        // takes a partly transparent pixel of a colour that is not black to separate them.
        //
        // Half-opaque pure red, premultiplied, is R = 255 * 128 / 255 = 128 with alpha 128.
        // Composited onto white the red channel saturates: 128 + 255 * 127 / 255 = 255.
        // Reading the same buffer as straight alpha would scale the source a second time and
        // give 64 + 127 = 191, a visibly duller red.
        var halfRed = new RasterImage(1, 1, [0, 0, 128, 128]);

        var flattened = SkiaRasterizer.FlattenOnto(halfRed, RgbColor.White);

        // BGRA: blue and green come from the white underneath, red is the saturating channel.
        Assert.Equal(127, flattened.Pixels[0]);
        Assert.Equal(127, flattened.Pixels[1]);
        Assert.Equal(255, flattened.Pixels[2]);
        Assert.Equal(255, flattened.Pixels[3]);
    }

    [Fact]
    public void FlattenOnto_LeavesAnOpaquePixelExactlyAsItWas()
    {
        // The other half of the same claim: only the transparent parts may change. A drawn
        // module is already opaque, so flattening must be a no-op on it -- an implementation
        // that treated the buffer as straight rather than premultiplied alpha would wash every
        // one of them out towards the background.
        var image = SkiaRasterizer.Render(Build(), 64);
        var flattened = SkiaRasterizer.FlattenOnto(image, RgbColor.FromRgb(0xFF, 0x00, 0x00));

        var opaque = -1;
        for (var i = 3; i < image.Pixels.Length; i += 4)
        {
            if (image.Pixels[i] == 255)
            {
                opaque = i - 3;
                break;
            }
        }

        Assert.True(opaque >= 0, "the rendered code had no opaque pixel, so this proves nothing");
        Assert.Equal(image.Pixels[opaque + 0], flattened.Pixels[opaque + 0]);
        Assert.Equal(image.Pixels[opaque + 1], flattened.Pixels[opaque + 1]);
        Assert.Equal(image.Pixels[opaque + 2], flattened.Pixels[opaque + 2]);
    }
}
