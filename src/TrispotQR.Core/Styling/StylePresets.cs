using TrispotQR.Core.Primitives;

namespace TrispotQR.Core.Styling;

/// <summary>A named look, shown as a clickable card in the Look step.</summary>
/// <param name="Name">Short label under the thumbnail.</param>
/// <param name="Description">Tooltip text, in plain English.</param>
/// <param name="Style">The style applied when the card is clicked.</param>
/// <param name="IsBuiltIn">Built-in presets cannot be renamed or deleted.</param>
public sealed record StylePreset(string Name, string Description, QrStyle Style, bool IsBuiltIn = false);

/// <summary>
/// The presets offered out of the box. Between them they cover the great majority of what
/// anyone actually wants, which is the point: a first-time user should be able to pick a
/// card and be finished without ever opening Advanced options.
///
/// Every one of these is covered by the scannability matrix in the test suite.
/// </summary>
public static class StylePresets
{
    /// <summary>
    /// The two colours behind the Two-tone preset. Both were checked against white and
    /// clear the contrast threshold comfortably, so the preset is a safe starting point
    /// for someone swapping in their own brand colours.
    /// </summary>
    private static readonly RgbColor DeepNavy = RgbColor.FromRgb(0x1B, 0x2A, 0x4A);
    private static readonly RgbColor WarmGold = RgbColor.FromRgb(0x8A, 0x6D, 0x3B);

    public static IReadOnlyList<StylePreset> BuiltIn { get; } =
    [
        new StylePreset(
            "Classic",
            "Plain black squares. The most reliable option, and the right choice when in doubt.",
            QrStyle.Default,
            IsBuiltIn: true),

        new StylePreset(
            "Rounded",
            "Softened squares. Looks a little friendlier while staying easy to scan.",
            QrStyle.Default with
            {
                ModuleShape = ModuleShape.RoundedSquare,
                ModuleScale = 0.92,
                MarkerFrameShape = MarkerFrameShape.RoundedSquare,
                MarkerCenterShape = MarkerCenterShape.RoundedSquare,
            },
            IsBuiltIn: true),

        new StylePreset(
            "Dots",
            "Round dots with a little space between them. Modern, and still scans well.",
            QrStyle.Default with
            {
                ModuleShape = ModuleShape.Circle,
                ModuleScale = 0.88,
                MarkerFrameShape = MarkerFrameShape.Circle,
                MarkerCenterShape = MarkerCenterShape.Circle,
            },
            IsBuiltIn: true),

        new StylePreset(
            "Fluid",
            "Neighbouring squares flow together into one shape. The most designed looking option.",
            QrStyle.Default with
            {
                ModuleShape = ModuleShape.Fluid,
                MarkerFrameShape = MarkerFrameShape.RoundedSquare,
                MarkerCenterShape = MarkerCenterShape.RoundedSquare,
            },
            IsBuiltIn: true),

        new StylePreset(
            "Outlined",
            "Each shape gets a thin light outline, which helps the code stand out on a busy photo.",
            QrStyle.Default with
            {
                ModuleShape = ModuleShape.RoundedSquare,
                ModuleScale = 0.9,
                MarkerFrameShape = MarkerFrameShape.RoundedSquare,
                MarkerCenterShape = MarkerCenterShape.RoundedSquare,
                Outline = new OutlineStyle
                {
                    Enabled = true,
                    Color = RgbColor.White,
                    ThicknessRatio = 0.07,
                    Target = OutlineTarget.Both,
                },
            },
            IsBuiltIn: true),

        new StylePreset(
            "Two-tone",
            "Navy code with gold corner rings. A starting point for your own colors.",
            QrStyle.Default with
            {
                ModuleShape = ModuleShape.Fluid,
                Foreground = DeepNavy,
                MarkerFrameColor = WarmGold,
                MarkerCenterColor = DeepNavy,
                MarkerFrameShape = MarkerFrameShape.RoundedSquare,
                MarkerCenterShape = MarkerCenterShape.Circle,
            },
            IsBuiltIn: true),
    ];

    /// <summary>The preset a first run starts on.</summary>
    public static StylePreset Default => BuiltIn[0];
}
