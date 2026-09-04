using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;
using TrispotQR.Core.Primitives;

namespace TrispotQR.Core.Rendering;

/// <summary>Raw pixels: BGRA, four bytes each, premultiplied, top row first.</summary>
public sealed record RasterImage(int Width, int Height, byte[] Pixels);

/// <summary>
/// Paints a <see cref="QrDrawing"/> into pixels.
///
/// Skia rather than any UI toolkit, because this has to work with no window, no display
/// and no UI thread: PNG export, the preset thumbnails and the scannability check all run
/// off-screen, and two of them run on a background thread. It also renders identically on
/// Windows, macOS and Linux, which a per-platform toolkit would not.
/// </summary>
public static class SkiaRasterizer
{
    /// <param name="backgroundOverride">
    /// Forces a background colour regardless of the drawing's own. Used to flatten a
    /// transparent code onto white before decoding, which is what a scanner would see.
    /// </param>
    public static RasterImage Render(QrDrawing drawing, int pixelSize, RgbColor? backgroundOverride = null)
    {
        ArgumentNullException.ThrowIfNull(drawing);

        if (pixelSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelSize), "Pixel size must be positive.");
        }

        // Premultiplied so a code with no background stays genuinely transparent rather
        // than being flattened onto white here.
        var info = new SKImageInfo(pixelSize, pixelSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale((float)(pixelSize / drawing.SizeInUnits));

        var background = backgroundOverride ?? drawing.Background;
        if (background is { } colour && colour.A > 0)
        {
            using var fill = new SKPaint { Color = ToSk(colour), IsAntialias = false, Style = SKPaintStyle.Fill };
            canvas.DrawRect(0, 0, (float)drawing.SizeInUnits, (float)drawing.SizeInUnits, fill);
        }

        foreach (var layer in drawing.Layers)
        {
            DrawLayer(canvas, layer);
        }

        DrawLogo(canvas, drawing);

        canvas.Restore();
        canvas.Flush();

        var pixels = new byte[info.BytesSize];

        using (var image = surface.Snapshot())
        using (var bitmap = SKBitmap.FromImage(image))
        {
            Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        }

        return new RasterImage(pixelSize, pixelSize, pixels);
    }

    public static byte[] EncodePng(RasterImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);

        try
        {
            using var bitmap = new SKBitmap();
            bitmap.InstallPixels(info, handle.AddrOfPinnedObject(), info.RowBytes);

            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }

    private static void DrawLayer(SKCanvas canvas, QrLayer layer)
    {
        if (layer.Path.IsEmpty)
        {
            return;
        }

        using var path = SkiaPath.ToSKPath(layer.Path);

        using var fill = new SKPaint
        {
            Color = ToSk(layer.Fill),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        canvas.DrawPath(path, fill);

        if (layer.Stroke is { } stroke)
        {
            using var pen = new SKPaint
            {
                Color = ToSk(stroke.Color),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)stroke.Thickness,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
            };
            canvas.DrawPath(path, pen);
        }
    }

    private static void DrawLogo(SKCanvas canvas, QrDrawing drawing)
    {
        if (drawing.Logo is not { } logo || !File.Exists(logo.Path))
        {
            return;
        }

        using var stream = File.OpenRead(logo.Path);
        using var bitmap = SKBitmap.Decode(stream);

        if (bitmap is null)
        {
            return;
        }

        canvas.DrawBitmap(
            bitmap,
            new SKRect((float)logo.X, (float)logo.Y, (float)(logo.X + logo.Width), (float)(logo.Y + logo.Height)),
            SKSamplingOptions.Default);
    }

    private static SKColor ToSk(RgbColor c) => new(c.R, c.G, c.B, c.A);
}
