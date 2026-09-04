using System.Text.Json.Serialization;
using System.Windows.Media;
using TrispotQR.Core.Qr;

namespace TrispotQR.Core.Styling;

/// <summary>Outline stroked around the drawn shapes.</summary>
public sealed record OutlineStyle
{
    public bool Enabled { get; init; }

    public Color Color { get; init; } = Colors.White;

    /// <summary>Stroke width as a fraction of one module. Kept relative so it scales with the code.</summary>
    public double ThicknessRatio { get; init; } = 0.08;

    public OutlineTarget Target { get; init; } = OutlineTarget.Both;

    public static OutlineStyle Off { get; } = new();
}

/// <summary>An image placed in the middle of the code.</summary>
public sealed record LogoStyle
{
    /// <summary>Path to the image file. Null means no logo.</summary>
    public string? Path { get; init; }

    /// <summary>Width of the logo as a fraction of the code width, excluding the quiet zone.</summary>
    public double SizeRatio { get; init; } = 0.22;

    public LogoPunchShape PunchShape { get; init; } = LogoPunchShape.RoundedSquare;

    /// <summary>Clear space around the logo, in module units.</summary>
    public double PunchPadding { get; init; } = 0.6;

    [JsonIgnore]
    public bool HasImage => !string.IsNullOrWhiteSpace(Path);

    public static LogoStyle None { get; } = new();
}

/// <summary>
/// The complete visual configuration of a code. A record so the UI and the tests can
/// derive variations with <c>with</c> expressions, and so presets are trivially
/// comparable for the "unsaved changes" state.
/// </summary>
public sealed record QrStyle
{
    public ModuleShape ModuleShape { get; init; } = ModuleShape.Square;

    /// <summary>
    /// Size of a drawn module relative to its cell, from 0.55 to 1.0. Below 1.0 leaves a
    /// gap between modules, which is what makes the dot styles read as dots.
    /// </summary>
    public double ModuleScale { get; init; } = 1.0;

    public MarkerFrameShape MarkerFrameShape { get; init; } = MarkerFrameShape.Square;

    public MarkerCenterShape MarkerCenterShape { get; init; } = MarkerCenterShape.Square;

    public Color Foreground { get; init; } = Colors.Black;

    /// <summary>Background colour, or null for a transparent background.</summary>
    public Color? Background { get; init; } = Colors.White;

    /// <summary>Colour of the marker rings, or null to inherit <see cref="Foreground"/>.</summary>
    public Color? MarkerFrameColor { get; init; }

    /// <summary>Colour of the marker cores, or null to inherit <see cref="Foreground"/>.</summary>
    public Color? MarkerCenterColor { get; init; }

    public OutlineStyle Outline { get; init; } = OutlineStyle.Off;

    /// <summary>
    /// Margin around the code in modules. The QR specification calls for 4. Going lower
    /// saves space but makes scanning less reliable against a busy background.
    /// </summary>
    public int QuietZoneModules { get; init; } = 4;

    public EccLevel Ecc { get; init; } = EccLevel.Medium;

    public LogoStyle Logo { get; init; } = LogoStyle.None;

    /// <summary>Width and height of the exported image in pixels.</summary>
    public int PixelSize { get; init; } = 1024;

    public static QrStyle Default { get; } = new();

    /// <summary>The colour the markers actually draw in, after inheritance.</summary>
    [JsonIgnore]
    public Color EffectiveMarkerFrameColor => MarkerFrameColor ?? Foreground;

    /// <summary>The colour the marker cores actually draw in, after inheritance.</summary>
    [JsonIgnore]
    public Color EffectiveMarkerCenterColor => MarkerCenterColor ?? Foreground;

    /// <summary>
    /// Clamps the free-form numeric values into ranges the renderer can honour, so a bad
    /// preset file or a stray binding cannot produce an unscannable or degenerate code.
    /// </summary>
    public QrStyle Normalised() => this with
    {
        ModuleScale = Math.Clamp(ModuleScale, 0.55, 1.0),
        QuietZoneModules = Math.Clamp(QuietZoneModules, 0, 8),
        PixelSize = Math.Clamp(PixelSize, 64, 4096),
        Outline = Outline with { ThicknessRatio = Math.Clamp(Outline.ThicknessRatio, 0.01, 0.25) },
        Logo = Logo with
        {
            SizeRatio = Math.Clamp(Logo.SizeRatio, 0.05, 0.40),
            PunchPadding = Math.Clamp(Logo.PunchPadding, 0, 3),
        },
    };
}
