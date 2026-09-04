using System.IO;
using TrispotQR.Core.Rendering;

namespace TrispotQR.Core.Export;

/// <summary>
/// Writes a rendered code out as a PNG. The encoder keeps whatever alpha the image
/// carries, so a code rendered with no background saves as a genuinely transparent file.
/// </summary>
public static class PngExporter
{
    public static void Save(RasterImage image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Written to a temporary file and moved into place so an interrupted save cannot
        // leave a half-written PNG where a good one used to be.
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, ToBytes(image));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The encoded PNG as bytes, for the clipboard and for tests.</summary>
    public static byte[] ToBytes(RasterImage image) => SkiaRasterizer.EncodePng(image);
}
