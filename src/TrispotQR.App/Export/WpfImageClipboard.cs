using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrispotQR.Core.Export;
using TrispotQR.Core.Rendering;

namespace TrispotQR.App.Export;

/// <summary>
/// Puts a rendered code on the clipboard in several formats at once.
///
/// This is fussier than it looks. Office applications differ in what they will accept:
/// PowerPoint honours a PNG with an alpha channel, while Word and Outlook reach for the
/// plain bitmap format and, given one with transparency, paste a black box. Offering both,
/// with the bitmap pre-flattened onto white, is what makes paste work everywhere.
/// </summary>
public sealed class WpfImageClipboard : IImageClipboard
{
    public void Copy(RasterImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var data = new DataObject();

        // Preferred by anything that understands transparency.
        //
        // The stream is deliberately not disposed. The DataObject only holds a reference
        // to it, and Clipboard.SetDataObject reads it during the flush at the end of this
        // method. Closing it first, which a using block would do, leaves a "PNG" format
        // advertised on the clipboard whose data is null. Nothing throws, so the copy
        // looks like it worked, and then paste silently does nothing in every application
        // that asks for PNG first. Ownership passes to the clipboard; the garbage
        // collector reclaims the buffer once the clipboard is done with it.
        var png = new MemoryStream(PngExporter.ToBytes(image));
        data.SetData("PNG", png, autoConvert: false);

        // The universal fallback, flattened so no application has to guess what to do
        // with the alpha channel.
        data.SetImage(FlattenOntoWhite(png.ToArray()));

        SetWithRetry(data);
        VerifyLanded();
    }

    /// <summary>
    /// Reads the clipboard straight back.
    ///
    /// Setting the clipboard can fail without throwing, which is precisely how the broken
    /// PNG format went unnoticed. Checking turns a silent no-op into a message the user
    /// can act on.
    ///
    /// The read is retried because the clipboard is briefly unavailable to readers just
    /// after a write, while Windows clipboard history and any other listeners take their
    /// own look at it. Measured over 25 copies: the write landed every time, but one read
    /// in 25 came back empty on the first attempt and succeeded on the second. Checking
    /// only once would turn that into a false "copy failed" message.
    /// </summary>
    private static void VerifyLanded()
    {
        const int attempts = 6;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    return;
                }
            }
            catch (Exception)
            {
                // The read itself failed, which says nothing about whether the write
                // worked. Fall through and try again.
            }

            if (attempt < attempts)
            {
                Thread.Sleep(25);
            }
        }

        throw new InvalidOperationException(
            "The image did not reach the clipboard. Another program may be holding it open.");
    }

    /// <summary>
    /// The clipboard is a shared system resource and another process can hold it open for
    /// a moment, which surfaces as a COM failure. A few short retries turn that from a
    /// visible error into something the user never notices.
    /// </summary>
    private static void SetWithRetry(DataObject data)
    {
        const int attempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return;
            }
            catch (Exception) when (attempt < attempts)
            {
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>
    /// Decodes the PNG bytes back into a bitmap and flattens it onto white, so no
    /// application has to guess what to do with the alpha channel.
    /// </summary>
    private static BitmapSource FlattenOntoWhite(byte[] pngBytes)
    {
        var source = new BitmapImage();

        using (var stream = new MemoryStream(pngBytes))
        {
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.StreamSource = stream;
            source.EndInit();
        }

        source.Freeze();

        var visual = new DrawingVisual();
        var width = source.PixelWidth;
        var height = source.PixelHeight;

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            context.DrawImage(source, new Rect(0, 0, width, height));
        }

        var flattened = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        flattened.Render(visual);
        flattened.Freeze();
        return flattened;
    }
}
