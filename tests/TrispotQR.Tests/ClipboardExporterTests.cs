using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrispotQR.App.Rendering;
using TrispotQR.Core.Export;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.Core.Validation;

namespace TrispotQR.Tests;

/// <summary>
/// Exercises the real system clipboard. Kept in the non-parallel UI collection because the
/// clipboard is a single shared resource and two tests writing to it at once would fight.
///
/// These exist because the clipboard path shipped broken: it advertised a PNG format whose
/// data was null, so applications that prefer PNG asked for it, got nothing, and pasted
/// nothing. Nothing threw, so only reading the clipboard back catches it.
///
/// Every test copies a payload unique to itself and then waits for the clipboard to hand
/// that exact code back. See <see cref="WaitForClipboard{T}"/> for why anything less is
/// unreliable.
/// </summary>
[Collection("UI")]
public class ClipboardExporterTests
{
    [Fact]
    public void Copy_PutsAPngOnTheClipboardThatStillDecodes()
    {
        const string payload = "https://www.example.org/png-check";

        var decoded = StaThread.Run(() =>
        {
            ClipboardExporter.Copy(Render(payload, QrStyle.Default));
            return QrDecoder.Decode(WaitForPng(payload));
        });

        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void Copy_AlsoPutsAPlainBitmapOnTheClipboardForOlderApplications()
    {
        const string payload = "https://www.example.org/bitmap-check";

        var decoded = StaThread.Run(() =>
        {
            ClipboardExporter.Copy(Render(payload, QrStyle.Default));
            return QrDecoder.Decode(WaitForBitmap(payload));
        });

        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void Copy_TransparentCode_KeepsTransparencyInThePngButNotInTheBitmap()
    {
        const string payload = "https://www.example.org/transparent-check";

        var (pngAlpha, bitmapAlpha) = StaThread.Run(() =>
        {
            ClipboardExporter.Copy(Render(payload, QrStyle.Default with { Background = null }));

            // The bitmap fallback is flattened onto white, so Word and Outlook cannot
            // paste it as a black box.
            return (CornerAlpha(WaitForPng(payload)), CornerAlpha(WaitForBitmap(payload)));
        });

        Assert.Equal(0, pngAlpha);
        Assert.Equal(255, bitmapAlpha);
    }

    [Fact]
    public void Copy_TwiceInARow_LeavesTheSecondCodeOnTheClipboard()
    {
        var decoded = StaThread.Run(() =>
        {
            ClipboardExporter.Copy(Render("first copy", QrStyle.Default));
            ClipboardExporter.Copy(Render("second copy", QrStyle.Default));

            return QrDecoder.Decode(WaitForPng("second copy"));
        });

        Assert.Equal("second copy", decoded);
    }

    /// <summary>
    /// Polls the clipboard until it produces what was just written.
    ///
    /// A read straight after a write is unreliable in three distinct ways, all of which
    /// were observed here. It can return null; it can return a stream that is empty or
    /// only partly filled, which surfaces from the imaging stack as
    /// "no imaging component suitable to complete this operation"; and it can still be
    /// showing the previous contents. Waiting on the specific condition, that the
    /// clipboard is handing back this exact code, covers all three, where retrying on
    /// null alone covers only the first.
    /// </summary>
    private static T WaitForClipboard<T>(Func<T?> read) where T : class
    {
        const int attempts = 40;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (read() is { } value)
                {
                    return value;
                }
            }
            catch (Exception) when (attempt < attempts)
            {
                // Transient. Fall through and try again.
            }

            if (attempt >= attempts)
            {
                throw new InvalidOperationException(
                    "The clipboard never produced the expected code after 40 attempts.");
            }

            Thread.Sleep(25);
        }
    }

    private static BitmapSource WaitForPng(string expectedPayload) => WaitForClipboard(() =>
    {
        if (Clipboard.GetData("PNG") is not MemoryStream stream || stream.Length == 0)
        {
            return null;
        }

        stream.Position = 0;
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        return QrDecoder.Decode(frame) == expectedPayload ? frame : null;
    });

    private static BitmapSource WaitForBitmap(string expectedPayload) => WaitForClipboard(() =>
    {
        var image = Clipboard.GetImage();
        return image is not null && QrDecoder.Decode(image) == expectedPayload ? image : null;
    });

    private static byte CornerAlpha(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[4];
        converted.CopyPixels(new Int32Rect(0, 0, 1, 1), pixels, 4, 0);
        return pixels[3];
    }

    private static RenderTargetBitmap Render(string payload, QrStyle style)
    {
        var matrix = QrEncoder.Encode(payload, style.Ecc).Matrix!;
        return WpfQrRenderer.RenderToBitmap(QrGeometryBuilder.Build(matrix, style), 512);
    }
}
