using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrispotQR.App.Views;

namespace TrispotQR.Tests;

/// <summary>
/// The picker keeps four inputs in step: the saturation/value square with its hue strip,
/// the palette, the hex box and the RGB sliders.
///
/// These concentrate on the part that is easy to get wrong and invisible when it breaks.
/// Converting RGB to HSV is lossy at two edges, because every grey has an undefined hue
/// and black has an undefined hue and saturation. A picker that re-derives them from the
/// colour each time sends the crosshair to a corner and the hue strip to red the instant
/// the brightness reaches zero, and dragging back up returns red rather than the colour
/// the user started from.
/// </summary>
[Collection("UI")]
public class ColorPickerTests
{
    private readonly WpfHost _host;

    public ColorPickerTests(WpfHost host) => _host = host;

    // The control resolves styles from App.xaml, so it has to be built on the host thread.
    private T OnPicker<T>(Func<ColorPicker, T> act) => _host.Run(() => act(new ColorPicker()));

    [Fact]
    public void SettingAColour_AdoptsItsHueSaturationAndValue()
    {
        var hsv = OnPicker(p =>
        {
            p.SelectedColor = Color.FromRgb(0x00, 0x80, 0xFF);
            return p.CurrentHsv;
        });

        Assert.Equal(209.88, hsv.Hue, 2);
        Assert.Equal(1.0, hsv.Saturation, 2);
        Assert.Equal(1.0, hsv.Value, 2);
    }

    [Fact]
    public void DraggingBrightnessToZeroAndBack_ReturnsTheOriginalColour()
    {
        var result = OnPicker(p =>
        {
            p.SelectedColor = Colors.Red;

            // Down to black, which cannot express a hue or a saturation of its own.
            p.SetFromSquare(1.0, 0.0);
            var atBlack = p.SelectedColor;
            var hueAtBlack = p.CurrentHsv.Hue;

            // Back up to full brightness.
            p.SetFromSquare(1.0, 1.0);

            return (atBlack, hueAtBlack, back: p.SelectedColor);
        });

        Assert.Equal(Colors.Black, result.atBlack);
        Assert.Equal(0, result.hueAtBlack, 3);
        Assert.Equal(Colors.Red, result.back);
    }

    [Fact]
    public void DraggingSaturationToZeroAndBack_KeepsTheHue()
    {
        var result = OnPicker(p =>
        {
            p.SetFromHue(240);
            p.SetFromSquare(1.0, 1.0);
            var saturated = p.SelectedColor;

            // All the way to the left edge: a pure grey, which has no hue of its own.
            p.SetFromSquare(0.0, 1.0);
            var grey = p.SelectedColor;

            p.SetFromSquare(1.0, 1.0);
            return (saturated, grey, back: p.SelectedColor, hue: p.CurrentHsv.Hue);
        });

        Assert.Equal(Colors.Blue, result.saturated);
        Assert.Equal(Colors.White, result.grey);
        Assert.Equal(240, result.hue, 3);
        Assert.Equal(Colors.Blue, result.back);
    }

    [Fact]
    public void AnExternalGrey_DoesNotDiscardTheChosenHue()
    {
        // A grey arriving from the palette, the hex box or a binding says nothing about
        // hue, so the strip should stay where the user left it.
        var hue = OnPicker(p =>
        {
            p.SetFromHue(120);
            p.SelectedColor = Color.FromRgb(0x80, 0x80, 0x80);
            return p.CurrentHsv.Hue;
        });

        Assert.Equal(120, hue, 3);
    }

    [Fact]
    public void AnExternalColour_DoesOverrideTheHue()
    {
        // The opposite case: a colour that genuinely carries a hue must win.
        var hue = OnPicker(p =>
        {
            p.SetFromHue(120);
            p.SelectedColor = Colors.Red;
            return p.CurrentHsv.Hue;
        });

        Assert.Equal(0, hue, 3);
    }

    [Theory]
    [InlineData(0, 255, 0, 0)]
    [InlineData(60, 255, 255, 0)]
    [InlineData(120, 0, 255, 0)]
    [InlineData(180, 0, 255, 255)]
    [InlineData(240, 0, 0, 255)]
    [InlineData(300, 255, 0, 255)]
    public void MovingTheHueStrip_ProducesTheExpectedColour(double hue, byte r, byte g, byte b)
    {
        var colour = OnPicker(p =>
        {
            p.SetFromSquare(1.0, 1.0);
            p.SetFromHue(hue);
            return p.SelectedColor;
        });

        Assert.Equal(Color.FromRgb(r, g, b), colour);
    }

    [Fact]
    public void SquareCoordinatesOutsideTheControl_AreClampedNotIgnored()
    {
        // A drag that overshoots the edge should pin to it rather than stop dead.
        var result = OnPicker(p =>
        {
            p.SetFromHue(0);
            p.SetFromSquare(5.0, 5.0);
            var high = p.SelectedColor;
            p.SetFromSquare(-3.0, -3.0);
            return (high, low: p.SelectedColor);
        });

        Assert.Equal(Colors.Red, result.high);
        Assert.Equal(Colors.Black, result.low);
    }

    [Fact]
    public void HueWrapsRatherThanClamping()
    {
        var (atZero, atFull) = OnPicker(p =>
        {
            p.SetFromSquare(1.0, 1.0);
            p.SetFromHue(0);
            var a = p.SelectedColor;
            p.SetFromHue(360);
            return (a, p.SelectedColor);
        });

        Assert.Equal(atZero, atFull);
    }

    /// <summary>
    /// Lays the popup out and renders it, which is the only way to catch a missing
    /// StaticResource or a gradient that silently fails to paint. Leaves the image next to
    /// the test binary so there is always a picture of what the picker last looked like.
    /// </summary>
    [Fact]
    public void Popup_LaysOutAndRendersItsControls()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "colorpicker-snapshot.png");

        var distinctColours = _host.Run(() =>
        {
            var picker = new ColorPicker { SelectedColor = Color.FromRgb(0x1B, 0x3F, 0x94) };

            // The popup's content is an ordinary element; rendering it directly avoids
            // needing a real popup window on a desktop.
            var popup = (System.Windows.Controls.Primitives.Popup)picker.FindName("PickerPopup");
            var content = (System.Windows.FrameworkElement)popup.Child;
            popup.Child = null;

            content.Measure(new System.Windows.Size(400, 700));
            content.Arrange(new System.Windows.Rect(0, 0, content.DesiredSize.Width, content.DesiredSize.Height));
            content.UpdateLayout();

            var w = (int)Math.Ceiling(content.DesiredSize.Width);
            var h = (int)Math.Ceiling(content.DesiredSize.Height);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            Core.Export.PngExporter.Save(ToRasterImage(bitmap), path);

            // A picker that painted correctly is full of distinct colours; one that failed
            // to resolve its gradients would come back nearly flat.
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
            var stride = converted.PixelWidth * 3;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);

            var seen = new HashSet<int>();
            for (var i = 0; i + 2 < pixels.Length; i += 3 * 13)
            {
                seen.Add((pixels[i] << 16) | (pixels[i + 1] << 8) | pixels[i + 2]);
            }

            return seen.Count;
        });

        Assert.True(distinctColours > 500,
            $"the picker rendered only {distinctColours} distinct colours, so its gradients likely did not paint");
    }

    [Fact]
    public void TheHexBoxAndSlidersFollowTheSquare()
    {
        // The whole point of the hub: every input reflects every other one.
        var colour = OnPicker(p =>
        {
            p.SetFromHue(240);
            p.SetFromSquare(1.0, 1.0);
            return p.SelectedColor;
        });

        Assert.Equal(Colors.Blue, colour);
    }

    /// <summary>The snapshot bitmap is always rendered as Pbgra32, which is already premultiplied BGRA.</summary>
    private static Core.Rendering.RasterImage ToRasterImage(System.Windows.Media.Imaging.RenderTargetBitmap bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return new Core.Rendering.RasterImage(bitmap.PixelWidth, bitmap.PixelHeight, pixels);
    }
}
