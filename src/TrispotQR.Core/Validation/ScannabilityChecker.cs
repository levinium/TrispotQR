using TrispotQR.Core.Primitives;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;

namespace TrispotQR.Core.Validation;

/// <summary>How confident we are that the current style will scan in the real world.</summary>
public enum ScanVerdict
{
    /// <summary>Read back cleanly and has no known risk factors.</summary>
    Good,

    /// <summary>Readable here, but carries something that often fails on a real camera.</summary>
    Risky,

    /// <summary>Did not read back at all, or could not be built.</summary>
    Bad,
}

/// <summary>The outcome of a scannability check, written to be shown to the user as is.</summary>
public sealed record ScanCheckResult(
    ScanVerdict Verdict,
    bool Decoded,
    string? DecodedText,
    double ContrastRatio,
    string Message);

/// <summary>
/// Decides whether a styled code is safe to use, by rendering it and reading it back with
/// a real barcode reader, then layering on the risks a clean synthetic render cannot show.
///
/// A perfect decode here is necessary but not sufficient: this render has ideal lighting,
/// no perspective and no print bleed. So low contrast, an inverted palette and a missing
/// quiet zone are flagged even when the decode succeeds, because those are exactly the
/// codes that read on a monitor and then fail on a printed flyer.
/// </summary>
public static class ScannabilityChecker
{
    /// <summary>Below this WCAG ratio, print and camera noise start eating the difference.</summary>
    private const double MinimumContrast = 3.0;

    /// <summary>The specification calls for 4 modules of clear space. Below 2 is trouble.</summary>
    private const int MinimumQuietZone = 2;

    public static ScanCheckResult Check(string text, QrStyle style)
    {
        style = style.Normalised();

        var encoded = QrEncoder.Encode(text, style.Ecc);
        if (!encoded.Success)
        {
            return new ScanCheckResult(ScanVerdict.Bad, false, null, 0, encoded.ErrorMessage!);
        }

        var drawing = QrGeometryBuilder.Build(encoded.Matrix!, style);
        return Check(drawing, text, style);
    }

    /// <summary>
    /// Checks a drawing that has already been built, so the app does not pay to encode and
    /// build the geometry a second time on every keystroke.
    /// </summary>
    public static ScanCheckResult Check(QrDrawing drawing, string expectedText, QrStyle style)
    {
        ArgumentNullException.ThrowIfNull(drawing);

        var decodedText = QrDecoder.Decode(
            SkiaRasterizer.Render(drawing, CheckSize(drawing), RgbColor.White));
        var decoded = string.Equals(decodedText, expectedText, StringComparison.Ordinal);

        var background = style.Background ?? RgbColor.White;
        var contrast = ContrastRatio(style.Foreground, background);
        var inverted = RelativeLuminance(style.Foreground) > RelativeLuminance(background);

        // Ordered most specific first, so the user is told the actual cause rather than
        // a generic "this did not scan".
        var issues = new List<string>();

        if (inverted)
        {
            issues.Add("This is a light code on a dark background. Plenty of phone cameras refuse to read those. "
                     + "Try a dark code on a light background.");
        }
        else if (contrast < MinimumContrast)
        {
            issues.Add($"There is not enough contrast between the code colour and the background "
                     + $"(currently {contrast:0.0} to 1). Aim for at least {MinimumContrast:0} to 1 by darkening "
                     + "the code or lightening the background.");
        }

        if (style.QuietZoneModules < MinimumQuietZone)
        {
            issues.Add("The margin around the code is too small. Scanners need clear space to find the code; "
                     + "4 is the standard. Raise the margin in Advanced options.");
        }

        if (style.Logo.HasImage && drawing.Logo is { CoverageRatio: > 0.25 })
        {
            issues.Add($"The logo covers {drawing.Logo.CoverageRatio:P0} of the code. Shrink it, or raise error "
                     + "correction to High so the code can survive the loss.");
        }

        if (!decoded)
        {
            var cause = issues.Count > 0
                ? issues[0]
                : "This code did not read back when tested. Try a plainer module shape, a smaller gap between "
                + "modules, or a smaller logo.";

            return new ScanCheckResult(ScanVerdict.Bad, false, decodedText, contrast, cause);
        }

        if (issues.Count > 0)
        {
            return new ScanCheckResult(ScanVerdict.Risky, true, decodedText, contrast, issues[0]);
        }

        return new ScanCheckResult(ScanVerdict.Good, true, decodedText, contrast, "Scannable.");
    }

    /// <summary>
    /// Enough pixels to give every module a fair chance without wasting time. Four pixels
    /// per module is comfortably above the decoder's limit, and a dense version 40 symbol
    /// therefore gets a bigger test render than a version 2 one.
    /// </summary>
    private static int CheckSize(QrDrawing drawing) =>
        (int)Math.Clamp(drawing.SizeInUnits * 4, 400, 1200);

    /// <summary>WCAG 2.1 contrast ratio, from 1 (identical) to 21 (black on white).</summary>
    public static double ContrastRatio(RgbColor a, RgbColor b)
    {
        var first = RelativeLuminance(a);
        var second = RelativeLuminance(b);
        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(RgbColor color) =>
        (0.2126 * Linearise(color.R)) + (0.7152 * Linearise(color.G)) + (0.0722 * Linearise(color.B));

    private static double Linearise(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
