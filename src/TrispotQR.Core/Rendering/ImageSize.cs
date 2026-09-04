using System.IO;
using SkiaSharp;

namespace TrispotQR.Core.Rendering;

/// <summary>
/// The pixel dimensions of an image file.
///
/// Only the header is read, not the pixels, because the one thing Core needs from a logo
/// file before drawing it is its aspect ratio. Returns null for a missing or unreadable
/// file, which is what lets an unusable logo degrade into a plain code rather than a crash.
/// </summary>
internal static class ImageSize
{
    public static (int Width, int Height)? Read(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);

            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            {
                return null;
            }

            return (codec.Info.Width, codec.Info.Height);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
