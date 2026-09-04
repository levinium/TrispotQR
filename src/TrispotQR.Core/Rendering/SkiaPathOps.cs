using SkiaSharp;
using TrispotQR.Core.Primitives;

namespace TrispotQR.Core.Rendering;

/// <summary>
/// Boolean operations on paths, the one piece of geometry work the model cannot do for
/// itself. Used only for the logo punch-out, and only when a logo is present.
/// </summary>
internal static class SkiaPathOps
{
    /// <summary>Everything in <paramref name="subject"/> that is not inside <paramref name="punch"/>.</summary>
    public static QrPath Exclude(QrPath subject, QrPath punch)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(punch);

        if (subject.IsEmpty || punch.IsEmpty)
        {
            return subject;
        }

        using var a = SkiaPath.ToSKPath(subject);
        using var b = SkiaPath.ToSKPath(punch);
        using var result = a.Op(b, SKPathOp.Difference);

        // Op returns null when it cannot resolve the operation. Keeping the original is the
        // safe failure: a logo drawn over unbroken modules still scans at high error
        // correction, whereas dropping the layer would lose the code entirely.
        return result is null ? subject : SkiaPath.ToQrPath(result, FillRuleOf(result));
    }

    /// <summary>
    /// The rule the result is actually drawn under, which is Skia's choice and not the
    /// subject's. A difference comes back as even-odd with the hole wound the same way
    /// round as the outline, so carrying the subject's non-zero rule across would fill the
    /// hole straight back in.
    /// </summary>
    private static QrFillRule FillRuleOf(SKPath path) =>
        path.FillType is SKPathFillType.EvenOdd or SKPathFillType.InverseEvenOdd
            ? QrFillRule.EvenOdd
            : QrFillRule.NonZero;
}
