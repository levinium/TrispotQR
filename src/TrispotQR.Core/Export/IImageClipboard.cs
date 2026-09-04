using TrispotQR.Core.Rendering;

namespace TrispotQR.Core.Export;

/// <summary>
/// Puts an image on the system clipboard.
///
/// An interface because every platform advertises image formats differently, and because
/// Core must stay free of anything assuming a desktop. The Windows implementation lives in
/// the app; Phase 2 adds an Avalonia one beside it.
/// </summary>
public interface IImageClipboard
{
    /// <summary>Copies the image, throwing if the clipboard could not be written.</summary>
    void Copy(RasterImage image);
}
