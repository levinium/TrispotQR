using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;

namespace TrispotQR.Core.Validation;

/// <summary>
/// Reads a QR code back out of rendered pixels. This is what turns "the style looks fine"
/// into "the style actually scans", and it is the check the whole styling feature rests on.
/// </summary>
public static class QrDecoder
{
    /// <summary>
    /// Decodes the bitmap, or returns null when no code could be read. Any alpha is
    /// flattened onto white first, matching what a camera would see on paper or a screen.
    ///
    /// Two decoder passes, because neither alone is right.
    ///
    /// The camera-like pass locates the code by hunting for its corner markers, the way a
    /// phone does. It is the more meaningful test, but it is also fussy on a clean
    /// synthetic render: measured over 300 plain black-on-white codes it wrongly failed 15
    /// of them, all perfectly valid. Reporting "does not scan" on a plain QR code destroys
    /// any trust in the badge, so a failure there is not taken as final.
    ///
    /// The pure pass reads the module grid directly, which suits an image we rendered
    /// ourselves. It misread none of those 300. It is not simply permissive either: it
    /// still refuses a code with its corner markers painted out, or with half of it erased.
    ///
    /// So a code counts as readable if either pass reads it, and the real-world risks a
    /// clean render cannot show, contrast, inversion, quiet zone, logo coverage, are
    /// checked explicitly elsewhere rather than being inferred from a decode failure.
    /// </summary>
    public static string? Decode(BitmapSource bitmap) =>
        DecodeStrict(bitmap) ?? Read(bitmap, pureBarcode: true);

    /// <summary>
    /// Decodes using only the camera-like pass, which locates the code by its corner
    /// markers. Stricter than <see cref="Decode"/> and prone to false failures on clean
    /// renders, so it is meant for the test suite, where every style shipped in the app is
    /// held to the higher bar, rather than for judging a user's own content.
    /// </summary>
    public static string? DecodeStrict(BitmapSource bitmap) => Read(bitmap, pureBarcode: false);

    private static string? Read(BitmapSource bitmap, bool pureBarcode)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        // Bgr24 drops the alpha channel by compositing over the bitmap's own background.
        // A transparent PNG has no background of its own, so it is flattened onto white
        // explicitly rather than relying on the conversion.
        var opaque = Flatten(bitmap);
        var stride = opaque.PixelWidth * 3;
        var pixels = new byte[stride * opaque.PixelHeight];
        opaque.CopyPixels(pixels, stride, 0);

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = false,
            Options = new ZXing.Common.DecodingOptions
            {
                PossibleFormats = new[] { BarcodeFormat.QR_CODE },
                TryHarder = true,
                PureBarcode = pureBarcode,
            },
        };

        var result = reader.Decode(pixels, opaque.PixelWidth, opaque.PixelHeight, RGBLuminanceSource.BitmapFormat.BGR24);
        return result?.Text;
    }

    private static BitmapSource Flatten(BitmapSource source)
    {
        var visual = new DrawingVisual();
        var width = source.PixelWidth;
        var height = source.PixelHeight;

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, width, height));
            context.DrawImage(source, new System.Windows.Rect(0, 0, width, height));
        }

        var flattened = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        flattened.Render(visual);

        var converted = new FormatConvertedBitmap(flattened, PixelFormats.Bgr24, null, 0);
        converted.Freeze();
        return converted;
    }
}
