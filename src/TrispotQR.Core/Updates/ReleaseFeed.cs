using System.Text.Json;

namespace TrispotQR.Core.Updates;

/// <summary>A file published with a release.</summary>
public sealed record ReleaseAsset(string Name, string Url, long Size);

/// <summary>A published release, reduced to what the decision, the installer and the notes popup need.</summary>
public sealed record ReleaseInfo(
    string? Tag,
    string? Url,
    bool IsDraft = false,
    bool IsPreRelease = false,
    IReadOnlyList<ReleaseAsset>? Assets = null,
    string? Notes = null);

/// <summary>
/// Reads GitHub's "latest release" document.
///
/// Hand read rather than deserialized into a type: the response carries dozens of fields this app
/// has no interest in, and binding to them would turn an unrelated upstream change into a parse
/// failure here.
/// </summary>
public static class ReleaseFeed
{
    /// <summary>Guards a text control against an enormous body; real notes come nowhere near it.</summary>
    public const int MaxNotesLength = 20000;

    public static ReleaseInfo? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // The tag is the version; the name is a title someone typed.
            var tag = Text(root, "tag_name") ?? Text(root, "name");
            if (tag is null)
            {
                return null;
            }

            var notes = Text(root, "body");
            if (notes is { Length: > MaxNotesLength })
            {
                // One unit earlier when the cut would keep the first half of a two-unit character alone.
                var length = char.IsHighSurrogate(notes[MaxNotesLength - 1]) ? MaxNotesLength - 1 : MaxNotesLength;
                notes = notes[..length];
            }

            return new ReleaseInfo(tag, Text(root, "html_url"), Flag(root, "draft"), Flag(root, "prerelease"), Assets(root), notes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Only assets with a name, a link and state "uploaded". GitHub lists an asset as soon as its
    /// upload starts, so a release being published right now can advertise a partial file.
    /// </summary>
    private static IReadOnlyList<ReleaseAsset> Assets(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var found = new List<ReleaseAsset>();

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = Text(asset, "name");
            var url = Text(asset, "browser_download_url");
            var state = Text(asset, "state");

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)
                || (state is not null && state != "uploaded"))
            {
                continue;
            }

            var size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var n) ? n : 0;
            found.Add(new ReleaseAsset(name, url, size));
        }

        return found;
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
