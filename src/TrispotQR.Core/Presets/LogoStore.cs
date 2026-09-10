using System.IO;
using System.Security.Cryptography;

namespace TrispotQR.Core.Presets;

/// <summary>
/// The app's own copies of the images used as logos, kept beside presets.json.
///
/// A saved style used to drop its logo on the way to disk, because the only thing it could
/// have recorded was the path the user picked the file from, and that is a path the app does
/// not control: renaming the folder, tidying a Downloads directory or saving from another
/// machine all turn it into a style that silently loses its logo. Keeping a copy is what makes
/// a saved logo actually survive.
///
/// Files are named by a hash of their own bytes, which buys deduplication for free: ten styles
/// sharing one logo store one file, and re-saving the same style over and over never
/// accumulates copies. It also means the name says nothing about where the file came from,
/// so a style file cannot leak the shape of someone's disk.
/// </summary>
public sealed class LogoStore
{
    private const string FolderName = "logos";

    /// <summary>
    /// Half a SHA-256, which is 128 bits. Far past the point where two different logos could
    /// collide by accident, and short enough that the folder stays readable.
    /// </summary>
    private const int NameLength = 32;

    /// <summary>
    /// Paths compare case-insensitively on Windows and case-sensitively everywhere else, and
    /// this type is asked whether two paths are the same file often enough for that to matter.
    /// </summary>
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static readonly StringComparer NameComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public LogoStore(string settingsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        Folder = Path.Combine(settingsDirectory, FolderName);
    }

    /// <summary>Where the copies live. Named Folder rather than Directory so it does not
    /// shadow <see cref="System.IO.Directory"/> inside this class.</summary>
    public string Folder { get; }

    /// <summary>
    /// Takes a copy of an image and returns the name to record, or null when there is no image.
    /// </summary>
    /// <remarks>
    /// Throws rather than swallowing an IO failure: the caller is in the middle of saving a
    /// style, and a style saved without the logo the user asked for is a worse outcome than
    /// being told the save did not work.
    /// </remarks>
    public string? Store(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        // Already one of ours. Saving a style whose logo came from another saved style must
        // reuse the copy rather than hash it into a second identical file.
        if (NameOf(sourcePath) is { } already)
        {
            return already;
        }

        var bytes = File.ReadAllBytes(sourcePath);
        var name = Convert.ToHexStringLower(SHA256.HashData(bytes))[..NameLength] + ExtensionOf(sourcePath);

        Directory.CreateDirectory(Folder);
        var destination = Path.Combine(Folder, name);

        // Identical bytes give an identical name, so an existing file is already the right
        // one and rewriting it would only risk truncating a file something else is reading.
        if (!File.Exists(destination))
        {
            File.WriteAllBytes(destination, bytes);
        }

        return name;
    }

    /// <summary>
    /// Turns a recorded name back into a full path, or null when the file is no longer there.
    /// </summary>
    /// <remarks>
    /// Only a bare file name resolves. A recorded value containing a directory is either a
    /// path written by a version that stored them, or a hand-edited file reaching for
    /// somewhere it should not; neither is something to open. Degrading to null rather than
    /// throwing is deliberate: a style whose logo has gone missing should still apply, minus
    /// the logo.
    /// </remarks>
    public string? Resolve(string? storedName)
    {
        if (string.IsNullOrWhiteSpace(storedName)
            || !string.Equals(Path.GetFileName(storedName), storedName, StringComparison.Ordinal))
        {
            return null;
        }

        var full = Path.Combine(Folder, storedName);

        // Also what rules out "." and "..", which survive the check above but are directories.
        return File.Exists(full) ? full : null;
    }

    /// <summary>
    /// The name to record for a path, or null when the path is not one of our copies.
    /// </summary>
    public string? NameOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var name = Path.GetFileName(path);

            return !string.IsNullOrEmpty(name)
                && string.Equals(Path.GetFullPath(path), Path.GetFullPath(Path.Combine(Folder, name)), PathComparison)
                    ? name
                    : null;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes every copy that nothing in <paramref name="inUse"/> refers to.
    /// </summary>
    /// <remarks>
    /// Called at startup rather than the moment a style is deleted, because the same logo can
    /// be in use by the style currently on screen: deleting the saved style that introduced it
    /// would otherwise pull the file out from under a live session. At startup the only things
    /// holding a logo are the saved styles and the restored session, and both are passed in.
    ///
    /// Failure is swallowed. An orphaned image costs a few kilobytes; refusing to open the app
    /// over one is not a trade worth making.
    /// </remarks>
    public void Sweep(IEnumerable<string?> inUse)
    {
        ArgumentNullException.ThrowIfNull(inUse);

        if (!Directory.Exists(Folder))
        {
            return;
        }

        var keep = inUse.Select(NameOf).OfType<string>().ToHashSet(NameComparer);

        try
        {
            foreach (var file in Directory.EnumerateFiles(Folder))
            {
                if (!keep.Contains(Path.GetFileName(file)))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The source file's extension when it is a plausible one, so the copies stay openable by
    /// double-clicking, and a neutral one otherwise. The extension is the only part of the
    /// user's path that survives into the stored name, so it is worth not carrying anything
    /// surprising through.
    /// </summary>
    private static string ExtensionOf(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        return extension.Length is > 1 and <= 6 && extension[1..].All(char.IsAsciiLetterOrDigit)
            ? extension
            : ".img";
    }
}
