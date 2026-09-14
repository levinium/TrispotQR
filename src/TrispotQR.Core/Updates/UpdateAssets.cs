using System.Globalization;

namespace TrispotQR.Core.Updates;

/// <summary>
/// Which of a release's files to fetch, whether what arrived is what was published, and which
/// addresses may be fetched at all. All string handling, which is where self-updates actually go
/// wrong, so all of it is tested without a network.
/// </summary>
public static class UpdateAssets
{
    public const string ExeName = "TrispotQR.exe";

    public const string ChecksumName = "TrispotQR.exe.sha256";

    /// <summary>
    /// A guard, not a tight bound: the exe is under 50 MB. Something answering the download with
    /// an endless stream fills the disk no further than this.
    /// </summary>
    public const long MostBytes = 256L * 1024 * 1024;

    /// <summary>Matched on the whole name: the checksum file's name contains the exe's.</summary>
    public static ReleaseAsset? Executable(IReadOnlyList<ReleaseAsset>? assets) => Named(assets, ExeName);

    public static ReleaseAsset? Checksum(IReadOnlyList<ReleaseAsset>? assets) => Named(assets, ChecksumName);

    /// <summary>
    /// Reads the hash from a sha256sum-format file: the hash, whitespace, the file name. Only
    /// the hash is taken, so CRLF endings, a BOM or a "*" binary marker still read.
    /// </summary>
    public static bool TryReadChecksum(string? text, out string hash)
    {
        hash = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var first = text.TrimStart('﻿')
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        var token = first?.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        if (token is null || token.Length != 64 || !token.All(char.IsAsciiHexDigit))
        {
            return false;
        }

        hash = token.ToLowerInvariant();
        return true;
    }

    public static string Format(byte[] hash) =>
        string.Concat(hash.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));

    /// <summary>Case insensitive: the two sides are written by different tools.</summary>
    public static bool Matches(string? expected, string? actual) =>
        !string.IsNullOrWhiteSpace(expected)
        && !string.IsNullOrWhiteSpace(actual)
        && string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// HTTPS, or plain HTTP to this machine only. The loopback exception exists so the end to
    /// end test can serve a release locally; a real address over plain HTTP could be swapped by
    /// anyone on the network path, checksum and all.
    /// </summary>
    public static bool IsAllowedUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }

    private static ReleaseAsset? Named(IReadOnlyList<ReleaseAsset>? assets, string name) =>
        assets?.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
}
