using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using TrispotQR.Core.Export;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.UI.Controls;

namespace TrispotQR.UI.Tests;

public class QrPreviewTests : IDisposable
{
    /// <summary>
    /// Neither black nor white, so a pixel wearing it in a rendered preview can only have come
    /// from the logo image and not from the code drawn under it.
    /// </summary>
    private static readonly RgbColor Magenta = RgbColor.FromRgb(0xFF, 0x00, 0xFF);

    private readonly string _directory;

    public QrPreviewTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"TrispotQR-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static QrDrawing BuildDrawing()
    {
        var encoded = QrEncoder.Encode("https://www.emanuelnyc.org", EccLevel.Medium);
        return QrGeometryBuilder.Build(encoded.Matrix!, StylePresets.BuiltIn[0].Style);
    }

    /// <summary>The same code, with <paramref name="logo"/> placed in the middle of it.</summary>
    private static QrDrawing BuildDrawingWithLogo(string logo)
    {
        var encoded = QrEncoder.Encode("https://www.emanuelnyc.org", EccLevel.High);
        var style = StylePresets.BuiltIn[0].Style with
        {
            Ecc = EccLevel.High,
            Logo = LogoStyle.None with { Path = logo, SizeRatio = 0.25 },
        };

        return QrGeometryBuilder.Build(encoded.Matrix!, style);
    }

    /// <summary>A solid block of <see cref="Magenta"/> on disk, as a real PNG file.</summary>
    private string WriteMagentaImage()
    {
        var path = Path.Combine(_directory, "logo.png");

        // A code drawn in one colour on the same colour is a flat rectangle of it, which saves
        // reaching for a second image library just to fill some pixels.
        var encoded = QrEncoder.Encode("logo", EccLevel.Low);
        var flat = QrStyle.Default with { Foreground = Magenta, Background = Magenta };
        PngExporter.Save(SkiaRasterizer.Render(QrGeometryBuilder.Build(encoded.Matrix!, flat), 96), path);

        return path;
    }

    private static WriteableBitmap Render(QrDrawing? drawing) => Render(drawing, 200, 200);

    private static WriteableBitmap Render(QrDrawing? drawing, int width, int height)
    {
        var preview = new QrPreview { Drawing = drawing };
        var window = new Window { Width = width, Height = height, Content = preview };
        window.Show();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        return frame!;
    }

    [AvaloniaFact]
    public void DrawsSomethingWhenGivenADrawing()
    {
        Assert.True(
            HasMoreThanOneColour(Render(BuildDrawing())),
            "the preview came out a single flat colour, so nothing was drawn");
    }

    [AvaloniaFact]
    public void DrawsNothingWhenTheDrawingIsNull()
    {
        Assert.False(HasMoreThanOneColour(Render(null)), "an empty preview should be blank");
    }

    [AvaloniaFact]
    public void ScalesTheDrawingUpToFillTheControl()
    {
        // The drawing is measured in module units, roughly 29 across for this payload, while
        // the control is 200 pixels. Without the scale transform the code would render as a
        // speck in the top-left, so this asserts that ink reaches the bottom quarter.
        Assert.True(
            HasInkBelow(Render(BuildDrawing()), 0.75),
            "no ink in the bottom quarter, so the drawing was not scaled to the control");
    }

    [AvaloniaFact]
    public void CentersTheSquareDrawingInsideANonSquareControl()
    {
        // A 300x200 (landscape) control: side = min(300, 200) = 200, so the drawing should
        // land as a 200x200 square with a 50px empty margin on the left and right and none on
        // top or bottom. This is deliberately not just "is there ink somewhere": a transform
        // that scales correctly but composes scale and translation in the wrong order still
        // produces a plausible-looking picture in a SQUARE window (translation is (0, 0) there,
        // so order cannot matter) but breaks visibly the moment the control is not square.
        //
        // Concretely, for this control and this payload (SizeInUnits = 33, so scale =
        // 200/33 ~= 6.06, and the centring offset is (50, 0)): composing translate-then-scale
        // instead of scale-then-translate multiplies that offset by scale too, shifting the
        // whole square by roughly 50 * 6.06 ~= 303px to the right -- past the 300px-wide
        // window entirely. So a backwards composition does not just mis-centre the drawing
        // here, it removes it from the frame, which both assertions below catch: the "ink
        // reaches the middle band" assertion fails outright (nothing is on screen to reach
        // it), and stretching the drawing to fill the full width -- the other way this control
        // commonly goes wrong -- would instead put ink where the margin assertion expects none.
        // Verified empirically: swapping the multiplication order in QrPreview.Render makes
        // this test fail (no ink anywhere in the frame) while leaving the other three tests
        // passing, which is exactly the gap the square-window tests could not see.
        const int width = 300;
        const int height = 200;
        const int side = height; // the smaller dimension
        const int margin = (width - side) / 2; // 50

        // A few pixels of inset around each boundary, so antialiasing on the edge of the
        // background rectangle cannot make an otherwise-correct render look like a failure.
        const int inset = 4;

        var bitmap = Render(BuildDrawing(), width, height);
        var pixels = ReadPixels(bitmap, out var size);
        var stride = pixels.Length / size.Height;
        var background = BitConverter.ToUInt32(pixels, 0); // (0, 0) is always in the left margin

        Assert.True(
            ColumnRangeIsBackgroundOnly(pixels, stride, size.Height, 0, margin - inset, background),
            "found ink in the left margin, so the drawing was not confined to a centred square");

        Assert.True(
            ColumnRangeIsBackgroundOnly(pixels, stride, size.Height, margin + side + inset, size.Width, background),
            "found ink in the right margin, so the drawing was not confined to a centred square");

        Assert.True(
            ColumnRangeHasInk(pixels, stride, size.Height, margin + inset, margin + side - inset, background),
            "no ink in the centred square, so the drawing was not drawn where the margins say it should be");
    }

    [AvaloniaFact]
    public void DrawsTheLogoOverTheCodeWhenThereIsOne()
    {
        // What the user sees while choosing a logo has to be what they will get when they save
        // it. Until this, the exporters composited the image and the preview did not, so a logo
        // could be sized and shaped against a picture of a code with a blank hole in the middle.
        var logo = WriteMagentaImage();

        Assert.True(
            MagentaPixels(Render(BuildDrawingWithLogo(logo))) > 0,
            "no logo-coloured pixel anywhere in the preview, so the image was never drawn");

        // The control: the same code with no logo has none of that colour in it, so the
        // assertion above cannot be satisfied by anything the code itself is painted with.
        Assert.Equal(0, MagentaPixels(Render(BuildDrawing())));
    }

    [AvaloniaFact]
    public void DrawsTheCodeAnywayWhenTheLogoFileHasGone()
    {
        // A path is remembered in the style and the file behind it can be moved, renamed or
        // deleted while the app is open. Core's exporters degrade to a plain code in that case;
        // the preview must do the same rather than throwing on every frame from then on.
        var logo = WriteMagentaImage();
        var drawing = BuildDrawingWithLogo(logo);
        File.Delete(logo);

        Assert.True(
            HasMoreThanOneColour(Render(drawing)),
            "the preview came out blank, so a missing logo file took the whole code with it");
    }

    /// <summary>
    /// How many pixels are the logo's colour. Compares the outer two channels loosely and the
    /// middle one strictly, which is what magenta looks like whether the buffer is laid out
    /// BGRA or RGBA -- red and blue swap places, green does not move.
    /// </summary>
    private static int MagentaPixels(WriteableBitmap bitmap)
    {
        var pixels = ReadPixels(bitmap, out _);
        var found = 0;

        for (var i = 0; i + 4 <= pixels.Length; i += 4)
        {
            if (pixels[i] > 200 && pixels[i + 2] > 200 && pixels[i + 1] < 80)
            {
                found++;
            }
        }

        return found;
    }

    private static bool ColumnRangeIsBackgroundOnly(
        byte[] pixels, int stride, int height, int xStart, int xEndExclusive, uint background)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = xStart; x < xEndExclusive; x++)
            {
                if (BitConverter.ToUInt32(pixels, (y * stride) + (x * 4)) != background)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ColumnRangeHasInk(
        byte[] pixels, int stride, int height, int xStart, int xEndExclusive, uint background)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = xStart; x < xEndExclusive; x++)
            {
                if (BitConverter.ToUInt32(pixels, (y * stride) + (x * 4)) != background)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasMoreThanOneColour(WriteableBitmap bitmap)
    {
        var pixels = ReadPixels(bitmap, out _);
        var first = BitConverter.ToUInt32(pixels, 0);

        for (var i = 4; i + 4 <= pixels.Length; i += 4)
        {
            if (BitConverter.ToUInt32(pixels, i) != first)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasInkBelow(WriteableBitmap bitmap, double fraction)
    {
        var pixels = ReadPixels(bitmap, out var size);
        var background = BitConverter.ToUInt32(pixels, 0);
        var stride = pixels.Length / size.Height;

        for (var y = (int)(size.Height * fraction); y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                if (BitConverter.ToUInt32(pixels, (y * stride) + (x * 4)) != background)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static byte[] ReadPixels(WriteableBitmap bitmap, out PixelSize size)
    {
        size = bitmap.PixelSize;
        using var buffer = bitmap.Lock();
        var bytes = new byte[buffer.RowBytes * size.Height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return bytes;
    }
}
