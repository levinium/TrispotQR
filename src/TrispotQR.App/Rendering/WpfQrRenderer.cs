using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Rendering;

namespace TrispotQR.App.Rendering;

/// <summary>
/// Paints a <see cref="QrDrawing"/>. The same two methods feed the on-screen preview, the
/// preset thumbnails, the PNG export and the scannability check, so what the user sees is
/// always exactly what gets saved.
///
/// THROWAWAY. Lives in the app rather than in Core so Core owes nothing to WPF. Phase 2
/// deletes it along with the rest of the WPF window.
/// </summary>
public static class WpfQrRenderer
{
    /// <summary>
    /// Builds a scalable visual. The drawing is authored in module units, so the single
    /// scale transform here is the only place output resolution is decided.
    /// </summary>
    /// <param name="backgroundOverride">
    /// Forces a background colour regardless of the drawing's own. Used to flatten a
    /// transparent code onto white before decoding, which is what a scanner would see.
    /// </param>
    public static DrawingVisual RenderToVisual(QrDrawing drawing, double pixelSize, RgbColor? backgroundOverride = null)
    {
        ArgumentNullException.ThrowIfNull(drawing);

        var visual = new DrawingVisual();
        var scale = pixelSize / drawing.SizeInUnits;

        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(scale, scale));

            var background = backgroundOverride ?? drawing.Background;
            if (background is { } colour && colour.A > 0)
            {
                var brush = WpfGeometryAdapter.ToBrush(colour);
                context.DrawRectangle(brush, null, new Rect(0, 0, drawing.SizeInUnits, drawing.SizeInUnits));
            }

            foreach (var layer in drawing.Layers)
            {
                context.DrawGeometry(
                    WpfGeometryAdapter.ToBrush(layer.Fill),
                    WpfGeometryAdapter.ToPen(layer.Stroke),
                    WpfGeometryAdapter.ToGeometry(layer.Path));
            }

            DrawLogo(context, drawing);

            context.Pop();
        }

        return visual;
    }

    /// <summary>
    /// Builds a resolution-free <see cref="DrawingImage"/>, used for the live preview and
    /// the preset thumbnails. Cheaper than rasterising, and it stays crisp when the window
    /// is resized because WPF redraws the vectors rather than scaling pixels.
    /// </summary>
    public static DrawingImage RenderToDrawingImage(QrDrawing drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);

        var bounds = new Rect(0, 0, drawing.SizeInUnits, drawing.SizeInUnits);
        var group = new DrawingGroup();

        // A fully transparent rectangle fixes the image's bounds to the whole canvas,
        // quiet zone included. Without it the image would crop to the drawn modules and
        // the margin would silently disappear from the preview.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(bounds)));

        if (drawing.Background is { A: > 0 } colour)
        {
            group.Children.Add(new GeometryDrawing(
                WpfGeometryAdapter.ToBrush(colour), null, new RectangleGeometry(bounds)));
        }

        foreach (var layer in drawing.Layers)
        {
            group.Children.Add(new GeometryDrawing(
                WpfGeometryAdapter.ToBrush(layer.Fill),
                WpfGeometryAdapter.ToPen(layer.Stroke),
                WpfGeometryAdapter.ToGeometry(layer.Path)));
        }

        if (drawing.Logo is { } logo && LoadImage(logo.Path) is { } image)
        {
            group.Children.Add(new ImageDrawing(image, new Rect(logo.X, logo.Y, logo.Width, logo.Height)));
        }

        group.Freeze();

        var result = new DrawingImage(group);
        result.Freeze();
        return result;
    }

    /// <summary>Renders to a square bitmap with a real alpha channel.</summary>
    public static RenderTargetBitmap RenderToBitmap(QrDrawing drawing, int pixelSize, RgbColor? backgroundOverride = null)
    {
        if (pixelSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelSize), "Pixel size must be positive.");
        }

        var visual = RenderToVisual(drawing, pixelSize, backgroundOverride);

        // Pbgra32 carries alpha, so a code with no background stays genuinely transparent
        // rather than being flattened onto white here.
        var bitmap = new RenderTargetBitmap(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void DrawLogo(DrawingContext context, QrDrawing drawing)
    {
        if (drawing.Logo is not { } logo)
        {
            return;
        }

        var image = LoadImage(logo.Path);
        if (image is null)
        {
            return;
        }

        context.DrawImage(image, new Rect(logo.X, logo.Y, logo.Width, logo.Height));
    }

    /// <summary>
    /// Loads an image fully into memory, or returns null when the file is missing or is
    /// not an image this system can decode. The app uses the null to tell the user their
    /// chosen logo cannot be read, rather than finding out at render time.
    ///
    /// OnLoad caching matters here: without it the file stays locked, and the user could
    /// not replace their logo file while the app is open.
    /// </summary>
    public static BitmapImage? LoadImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException
                                      or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
