using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace TrispotQR.Core.Updates;

/// <summary>
/// A released version, and whether one is newer than another.
///
/// Exists because the obvious comparison is wrong: ordered as text, "1.10.0" comes before
/// "1.9.0". A subset of semantic versioning: three numbers and an optional pre-release suffix.
/// Build metadata is parsed and discarded, because it takes no part in ordering.
/// </summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, string? PreRelease)
    : IComparable<ReleaseVersion>
{
    public bool IsPreRelease => !string.IsNullOrEmpty(PreRelease);

    /// <summary>
    /// Reads a version from a tag. Tolerates a leading "v" and surrounding space. "1" and "1.2"
    /// read as 1.0.0 and 1.2.0. Four parts are refused rather than truncated.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.Trim();
        if (span[0] is 'v' or 'V')
        {
            span = span[1..];
        }

        var plus = span.IndexOf('+');
        if (plus >= 0)
        {
            span = span[..plus];
        }

        string? pre = null;
        var dash = span.IndexOf('-');
        if (dash >= 0)
        {
            pre = span[(dash + 1)..];
            span = span[..dash];
            if (pre.Length == 0)
            {
                return false;
            }
        }

        var parts = span.Split('.');
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new ReleaseVersion(numbers[0], numbers[1], numbers[2], pre);
        return true;
    }

    public bool IsNewerThan(ReleaseVersion other) => CompareTo(other) > 0;

    public int CompareTo(ReleaseVersion other)
    {
        if (Major != other.Major)
        {
            return Major.CompareTo(other.Major);
        }

        if (Minor != other.Minor)
        {
            return Minor.CompareTo(other.Minor);
        }

        return Patch != other.Patch
            ? Patch.CompareTo(other.Patch)
            : ComparePreRelease(PreRelease, other.PreRelease);
    }

    /// <summary>
    /// Semver pre-release ordering. A version WITH a suffix is older than the same version
    /// without one: getting that backwards offers people on 1.2.0 a downgrade to 1.2.0-rc.1.
    /// </summary>
    private static int ComparePreRelease(string? left, string? right)
    {
        var leftFinal = string.IsNullOrEmpty(left);
        var rightFinal = string.IsNullOrEmpty(right);

        if (leftFinal && rightFinal)
        {
            return 0;
        }

        if (leftFinal)
        {
            return 1;
        }

        if (rightFinal)
        {
            return -1;
        }

        var a = left!.Split('.');
        var b = right!.Split('.');

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNumeric = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var an);
            var bNumeric = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var bn);

            if (aNumeric && bNumeric)
            {
                if (an != bn)
                {
                    return an.CompareTo(bn);
                }

                continue;
            }

            // Numeric identifiers rank below alphanumeric ones.
            if (aNumeric)
            {
                return -1;
            }

            if (bNumeric)
            {
                return 1;
            }

            var text = string.CompareOrdinal(a[i], b[i]);
            if (text != 0)
            {
                return text < 0 ? -1 : 1;
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    public override string ToString() =>
        IsPreRelease ? $"{Major}.{Minor}.{Patch}-{PreRelease}" : $"{Major}.{Minor}.{Patch}";
}
