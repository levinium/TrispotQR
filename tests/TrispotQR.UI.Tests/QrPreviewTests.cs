using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.UI.Controls;

namespace TrispotQR.UI.Tests;

public class QrPreviewTests
{
    private static QrDrawing BuildDrawing()
    {
        var encoded = QrEncoder.Encode("https://www.emanuelnyc.org", EccLevel.Medium);
        return QrGeometryBuilder.Build(encoded.Matrix!, StylePresets.BuiltIn[0].Style);
    }

    private static WriteableBitmap Render(QrDrawing? drawing)
    {
        var preview = new QrPreview { Drawing = drawing };
        var window = new Window { Width = 200, Height = 200, Content = preview };
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
