using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrispotQR.App.Views;
using TrispotQR.Core.Export;

namespace TrispotQR.Tests;

/// <summary>
/// The warning dialog is only ever built when someone tries to export a code that does not
/// scan, which means a mistake in its XAML would stay hidden until exactly the moment it
/// was most needed. These lay it out and render it so that cannot happen.
/// </summary>
[Collection("UI")]
public class ConfirmWindowTests
{
    private readonly WpfHost _host;

    public ConfirmWindowTests(WpfHost host) => _host = host;

    [Theory]
    [InlineData(true, "did-not-scan")]
    [InlineData(false, "may-not-scan")]
    public void Dialog_LaysOutAndRenders(bool severe, string snapshot)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"confirm-{snapshot}.png");

        var varied = _host.Run(() =>
        {
            var window = new ConfirmWindow(
                severe ? "This code did not scan" : "This code may not scan",
                "There is not enough contrast between the code colour and the background "
                + "(currently 1.7 to 1). Aim for at least 3 to 1 by darkening the code or "
                + "lightening the background.\n\nSave it anyway?",
                "Save anyway",
                defaultToProceed: !severe,
                severe);

            // Rendered through its detached content rather than by showing a real dialog,
            // which would block the test waiting for a click.
            var content = (FrameworkElement)window.Content;
            window.Content = null;
            content.Resources = window.Resources;

            content.Measure(new Size(430, 400));
            content.Arrange(new Rect(0, 0, 430, content.DesiredSize.Height));
            content.UpdateLayout();

            var h = (int)Math.Ceiling(content.DesiredSize.Height);
            var bitmap = new RenderTargetBitmap(430, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            PngExporter.Save(bitmap, path);

            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
            var stride = converted.PixelWidth * 3;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);

            var seen = new HashSet<int>();
            for (var i = 0; i + 2 < pixels.Length; i += 3 * 7)
            {
                seen.Add((pixels[i] << 16) | (pixels[i + 1] << 8) | pixels[i + 2]);
            }

            return seen.Count;
        });

        Assert.True(varied > 20, $"the dialog rendered only {varied} distinct colours, so it likely did not paint");
    }
}
