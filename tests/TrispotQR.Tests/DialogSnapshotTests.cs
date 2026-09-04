using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrispotQR.App.Services;
using TrispotQR.App.Views;
using TrispotQR.Core.Export;
using TrispotQR.Core.Presets;
using TrispotQR.Core.Rendering;

namespace TrispotQR.Tests;

/// <summary>
/// Lays out and renders the secondary windows in both themes, leaving a picture of each
/// beside the test binary.
///
/// These windows are only built in response to something the user does, so a mistake in one
/// stays invisible until exactly then. Rendering them here means a broken layout or an
/// unresolved resource fails the build instead.
/// </summary>
[Collection("UI")]
public class DialogSnapshotTests
{
    private readonly WpfHost _host;

    public DialogSnapshotTests(WpfHost host) => _host = host;

    public static TheoryData<AppTheme> Themes => new() { AppTheme.Light, AppTheme.Dark };

    [Theory]
    [MemberData(nameof(Themes))]
    public void SettingsWindow_Renders(AppTheme theme) =>
        Render(theme, "settings", 470, () => new SettingsWindow(AppSettings.Default));

    [Theory]
    [MemberData(nameof(Themes))]
    public void AboutWindow_Renders(AppTheme theme) =>
        Render(theme, "about", 440, () => new AboutWindow());

    private void Render(AppTheme theme, string name, int width, Func<Window> create)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"{name}-{theme}.png".ToLowerInvariant());

        var varied = _host.Run(() =>
        {
            ThemeManager.Apply(theme);

            try
            {
                var window = create();
                var content = (FrameworkElement)window.Content;
                window.Content = null;
                content.Resources = window.Resources;

                content.Measure(new Size(width, 1400));
                var height = (int)Math.Ceiling(content.DesiredSize.Height);
                content.Arrange(new Rect(0, 0, width, height));
                content.UpdateLayout();

                // Painted onto the window's own background first, because the content
                // itself is mostly transparent and would otherwise render onto nothing.
                var surface = new Border
                {
                    Background = window.Background,
                    Width = width,
                    Height = height,
                    Child = content,
                    Resources = window.Resources,
                };
                surface.Measure(new Size(width, height));
                surface.Arrange(new Rect(0, 0, width, height));
                surface.UpdateLayout();

                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                PngExporter.Save(ToRasterImage(bitmap), path);

                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
                var stride = converted.PixelWidth * 3;
                var pixels = new byte[stride * converted.PixelHeight];
                converted.CopyPixels(pixels, stride, 0);

                var seen = new HashSet<int>();
                for (var i = 0; i + 2 < pixels.Length; i += 3 * 11)
                {
                    seen.Add((pixels[i] << 16) | (pixels[i + 1] << 8) | pixels[i + 2]);
                }

                return seen.Count;
            }
            finally
            {
                ThemeManager.Apply(AppTheme.Light);
            }
        });

        Assert.True(varied > 15, $"{name} in {theme} rendered only {varied} distinct colours");
    }

    /// <summary>The snapshot bitmap is always rendered as Pbgra32, which is already premultiplied BGRA.</summary>
    private static RasterImage ToRasterImage(RenderTargetBitmap bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return new RasterImage(bitmap.PixelWidth, bitmap.PixelHeight, pixels);
    }
}
