using Avalonia.Controls;
using Avalonia.Input;
using TrispotQR.Core.Export;
using TrispotQR.Core.Rendering;

namespace TrispotQR.UI.Services;

/// <summary>
/// Puts a rendered code on the system clipboard as PNG bytes.
///
/// PNG rather than a raw bitmap because it keeps transparency, which is the whole reason the
/// app offers a transparent background. Avalonia has no per-platform bitmap clipboard format
/// the way WPF does, so unlike the WPF implementation there is no second flattened-on-white
/// entry; whether that is needed on each platform is a question for Phase 2c, when the
/// Avalonia build is actually pasted into Word and PowerPoint.
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
        var format = DataFormat.CreateBytesPlatformFormat("PNG");
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(format, PngExporter.ToBytes(image)));

        // Sync over async, deliberately. IImageClipboard.Copy is synchronous because the view
        // model's copy command is, and the clipboard call must run on the UI thread. Waiting on
        // the task outright would deadlock, since the work it needs is queued on this very
        // thread, so the dispatcher is pumped while waiting. The timeout turns a wedged
        // clipboard into an exception the view model already knows how to report, rather than a
        // frozen window.
        DispatcherWait.For(clipboard.SetDataAsync(data), Timeout);
    }
}
