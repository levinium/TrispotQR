using System.IO;
using System.Windows.Media.Imaging;

namespace TrispotQR.Core.Export;

/// <summary>
/// Writes a rendered code out as a PNG. The encoder keeps whatever alpha the bitmap
/// carries, so a code rendered with no background saves as a genuinely transparent file.
/// </summary>
public static class PngExporter
{
    public static void Save(BitmapSource bitmap, string path)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Written to a temporary file and moved into place so an interrupted save cannot
        // leave a half-written PNG where a good one used to be.
        var temporary = path + ".tmp";

        using (var stream = File.Create(temporary))
        {
            Encoder(bitmap).Save(stream);
        }

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The encoded PNG as bytes, for the clipboard and for tests.</summary>
    public static byte[] ToBytes(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        using var stream = new MemoryStream();
        Encoder(bitmap).Save(stream);
        return stream.ToArray();
    }

    private static PngBitmapEncoder Encoder(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder { Interlace = PngInterlaceOption.Off };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        return encoder;
    }
}
