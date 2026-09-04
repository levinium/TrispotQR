using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrispotQR.App.Rendering;
using TrispotQR.Core.Export;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.Core.Validation;

namespace TrispotQR.Tests;

/// <summary>
/// The loop that proves a style is usable: encode, render, then decode the pixels back
/// with a real barcode reader and check the text survived.
/// </summary>
public class RenderAndDecodeTests
{
    private const string Payload = "https://www.example.org";

    [Fact]
    public void Render_DefaultStyle_DecodesBackToTheOriginalText()
    {
        var decoded = StaThread.Run(() =>
        {
            var drawing = BuildDrawing(QrStyle.Default);
            var image = SkiaRasterizer.Render(drawing, 512);
            return QrDecoder.Decode(image);
        });

        Assert.Equal(Payload, decoded);
    }

    [Fact]
    public void Render_TransparentBackground_ProducesGenuinelyTransparentCorners()
    {
        var alpha = StaThread.Run(() =>
        {
            var drawing = BuildDrawing(QrStyle.Default with { Background = null });
            var bitmap = WpfQrRenderer.RenderToBitmap(drawing, 256);

            // Top-left pixel sits in the quiet zone, so it is background and nothing else.
            var pixels = new byte[4];
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            converted.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixels, 4, 0);
            return pixels[3];
        });

        Assert.Equal(0, alpha);
    }

    [Fact]
    public void Render_WhiteBackground_ProducesOpaqueCorners()
    {
        var pixel = StaThread.Run(() =>
        {
            var drawing = BuildDrawing(QrStyle.Default with { Background = RgbColor.White });
            var bitmap = WpfQrRenderer.RenderToBitmap(drawing, 256);

            var pixels = new byte[4];
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            converted.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixels, 4, 0);
            return pixels;
        });

        Assert.Equal(255, pixel[3]);
        Assert.Equal(255, pixel[0]);
        Assert.Equal(255, pixel[1]);
        Assert.Equal(255, pixel[2]);
    }

    [Fact]
    public void Render_HonoursTheRequestedPixelSize()
    {
        var (width, height) = StaThread.Run(() =>
        {
            var bitmap = WpfQrRenderer.RenderToBitmap(BuildDrawing(QrStyle.Default), 777);
            return (bitmap.PixelWidth, bitmap.PixelHeight);
        });

        Assert.Equal(777, width);
        Assert.Equal(777, height);
    }

    [Fact]
    public void PngExporter_WritesAFileWithThePngSignatureAndTheRightSize()
    {
        var path = Path.Combine(Path.GetTempPath(), $"TrispotQR-{Guid.NewGuid():N}.png");

        try
        {
            StaThread.Run(() =>
            {
                var image = SkiaRasterizer.Render(BuildDrawing(QrStyle.Default), 320);
                PngExporter.Save(image, path);
                return true;
            });

            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 100);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes.Take(4).ToArray());

            var reloaded = StaThread.Run(() =>
            {
                var decoder = new PngBitmapDecoder(
                    new Uri(path),
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                return (decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight);
            });

            Assert.Equal(320, reloaded.PixelWidth);
            Assert.Equal(320, reloaded.PixelHeight);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PngExporter_SavedTransparentFile_StillDecodesOnceCompositedOnWhite()
    {
        var path = Path.Combine(Path.GetTempPath(), $"TrispotQR-{Guid.NewGuid():N}.png");

        try
        {
            var decoded = StaThread.Run(() =>
            {
                var drawing = BuildDrawing(QrStyle.Default with { Background = null });
                PngExporter.Save(SkiaRasterizer.Render(drawing, 512), path);

                var frame = new PngBitmapDecoder(
                    new Uri(path),
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad).Frames[0];

                return QrDecoder.Decode(ToRasterImage(frame));
            });

            Assert.Equal(Payload, decoded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ScannabilityChecker_DefaultStyle_ReportsGood()
    {
        var result = StaThread.Run(() => ScannabilityChecker.Check(Payload, QrStyle.Default));

        Assert.Equal(ScanVerdict.Good, result.Verdict);
        Assert.True(result.Decoded);
        Assert.True(result.ContrastRatio > 20);
    }

    [Fact]
    public void ScannabilityChecker_LowContrast_WarnsEvenWhenTheDecodeSucceeds()
    {
        var style = QrStyle.Default with
        {
            Foreground = RgbColor.FromRgb(0xC8, 0xC8, 0xC8),
            Background = RgbColor.White,
        };

        var result = StaThread.Run(() => ScannabilityChecker.Check(Payload, style));

        Assert.NotEqual(ScanVerdict.Good, result.Verdict);
        Assert.Contains("contrast", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScannabilityChecker_InvertedColours_AreFlaggedBecauseManyScannersRejectThem()
    {
        // Light modules on a dark background have plenty of contrast but are the classic
        // way a good looking code fails on a real phone.
        var style = QrStyle.Default with { Foreground = RgbColor.White, Background = RgbColor.Black };

        var result = StaThread.Run(() => ScannabilityChecker.Check(Payload, style));

        Assert.NotEqual(ScanVerdict.Good, result.Verdict);
    }

    [Fact]
    public void ScannabilityChecker_NoQuietZone_IsFlaggedAsRisky()
    {
        var style = QrStyle.Default with { QuietZoneModules = 0 };

        var result = StaThread.Run(() => ScannabilityChecker.Check(Payload, style));

        Assert.NotEqual(ScanVerdict.Good, result.Verdict);
        Assert.Contains("margin", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScannabilityChecker_EmptyContent_ReportsTheEncodeProblemRatherThanCrashing()
    {
        var result = StaThread.Run(() => ScannabilityChecker.Check("", QrStyle.Default));

        Assert.Equal(ScanVerdict.Bad, result.Verdict);
        Assert.False(result.Decoded);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    private static QrDrawing BuildDrawing(QrStyle style)
    {
        var matrix = QrEncoder.Encode(Payload, style.Ecc).Matrix!;
        return QrGeometryBuilder.Build(matrix, style);
    }

    /// <summary>WPF's Pbgra32 is premultiplied BGRA, exactly what RasterImage carries.</summary>
    private static RasterImage ToRasterImage(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return new RasterImage(converted.PixelWidth, converted.PixelHeight, pixels);
    }
}
