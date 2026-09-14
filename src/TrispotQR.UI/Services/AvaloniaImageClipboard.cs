using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using TrispotQR.Core.Export;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Rendering;

namespace TrispotQR.UI.Services;

/// <summary>
/// Puts a rendered code on the system clipboard in two formats at once.
///
/// PNG keeps the transparency that is the whole reason the app offers a transparent
/// background, and anything that understands alpha reaches for it first. But Word, Outlook and
/// Excel reach for the plain bitmap instead, and a PNG-only clipboard is one they will not
/// paste at all. That was the state this shipped in: the copy looked like it worked and
/// nothing came out the other end.
///
/// So the second entry is a copy flattened onto white. Flattened rather than handed over with
/// its alpha intact because those same applications, given transparency in a bitmap, paste a
/// black box. This mirrors what WpfImageClipboard has always done, for the same reasons its
/// own comment gives.
/// </summary>
public sealed class AvaloniaImageClipboard(TopLevel topLevel) : IImageClipboard
{
    /// <summary>How long to wait for the clipboard before calling it stuck.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly TopLevel _topLevel = topLevel ?? throw new ArgumentNullException(nameof(topLevel));

    public void Copy(RasterImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var clipboard = _topLevel.Clipboard
            ?? throw new InvalidOperationException("This window has no clipboard.");

        // Avalonia 12.1.2's clipboard takes an IAsyncDataTransfer, not the DataObject the
        // brief's WPF-flavoured sketch used (that type is obsolete here anyway). DataTransfer
        // implements IAsyncDataTransfer itself -- it just always resolves synchronously -- so
        // it can go straight to SetDataAsync with no separate sync/async wrapper.
        //
        // CreateBytesPlatformFormat, not CreateBytesApplicationFormat: the "application" family
        // deliberately does NOT pass the identifier to the underlying platform (Avalonia's own
        // doc comment says so) -- it namespace-prefixes it so two Avalonia apps can share data
        // with each other, which is useless here. WpfImageClipboard puts the literal name "PNG"
        // on the real Win32 clipboard, because that is the format Word, PowerPoint and browsers
        // actually probe for on paste. CreateBytesPlatformFormat is the one whose doc comment
        // says the identifier is passed through as-is, so "PNG" reaches the platform the same
        // way it does from WPF.
        Place(clipboard.SetDataAsync, clipboard.FlushAsync, BuildTransfer(image));
    }

    /// <summary>
    /// Puts a transfer on the clipboard and then flushes it there.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="Copy"/> so the flush can be asserted. Nothing headless can see
    /// the Windows clipboard, and leaving the flush out breaks copy and paste across the whole
    /// machine rather than anything in this app, so it is exactly the line a later tidy-up could
    /// delete with every test still green.
    ///
    /// The two operations arrive as delegates rather than as the clipboard itself because
    /// Avalonia makes <c>IClipboard</c> unimplementable outside Avalonia: its reference assembly
    /// carries a hidden member no user type can supply, so a recording fake does not compile.
    /// </remarks>
    internal static void Place(Func<IAsyncDataTransfer?, Task> setData, Func<Task> flush, IAsyncDataTransfer data)
    {
        // Sync over async, deliberately. IImageClipboard.Copy is synchronous because the view
        // model's copy command is, and the clipboard call must run on the UI thread. Waiting on
        // the task outright would deadlock, since the work it needs is queued on this very
        // thread, so the dispatcher is pumped while waiting. The timeout turns a wedged
        // clipboard into an exception the view model already knows how to report, rather than a
        // frozen window.
        DispatcherWait.For(setData(data), Timeout);

        // Then flush, which is not optional. On Windows SetDataAsync hands the clipboard a live
        // COM object that stays in this process (OleSetClipboard) and renders each format only
        // when somebody asks for it. Left like that, every clipboard operation on the machine
        // has to call into this app first: other apps pasting, but also other apps COPYING,
        // since replacing the clipboard notifies its current owner, and the clipboard history
        // and PowerToys watchers that read every change. Measured with the process frozen for
        // eight seconds right after a copy: another app's plain text copy blocked for 5.5
        // seconds and then silently failed, leaving nothing to paste. Any stall in this app
        // became broken copy and paste everywhere, and the copied image vanished outright the
        // moment the app closed.
        //
        // FlushAsync (OleFlushClipboard) renders every format onto the system clipboard now and
        // releases the object, so the clipboard stops depending on this process at all. It is
        // what WpfImageClipboard's SetDataObject(data, copy: true) always did, which is why the
        // WPF app never had this problem.
        DispatcherWait.For(flush(), Timeout);
    }

    /// <summary>
    /// The formats a copy puts on the clipboard.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="Copy"/> so the set of formats can be asserted without a
    /// clipboard at all. Avalonia.Headless's clipboard accepts a write and reports success, so a
    /// test that only copied could not tell a two-format transfer from a one-format one, which
    /// is precisely the difference between pasting into Word and not.
    /// </remarks>
    internal static DataTransfer BuildTransfer(RasterImage image)
    {
        var data = new DataTransfer();

        data.Add(DataTransferItem.Create(
            DataFormat.CreateBytesPlatformFormat("PNG"), PngExporter.ToBytes(image)));

        // The fallback the Office applications actually take. DataFormat.Bitmap wants an
        // Avalonia Bitmap rather than bytes, and Avalonia converts that to whatever the
        // platform's own image format is, so there is no hand-rolled DIB here.
        //
        // Flattening happens in Core on the pixels rather than by re-rendering, because this
        // class is handed a finished image and never sees the drawing behind it. Encoding to PNG
        // only to decode it straight back is the cost of Bitmap having no constructor over raw
        // pixels; the decode is eager, so disposing the stream here is safe.
        var flattened = SkiaRasterizer.FlattenOnto(image, RgbColor.White);
        using var stream = new MemoryStream(PngExporter.ToBytes(flattened));
        data.Add(DataTransferItem.Create(DataFormat.Bitmap, new Bitmap(stream)));

        return data;
    }
}
