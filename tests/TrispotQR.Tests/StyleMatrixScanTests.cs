using System.Windows.Media;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.Core.Validation;

namespace TrispotQR.Tests;

/// <summary>
/// The guardrail behind the whole styling feature. Every combination the UI can produce is
/// rendered and then read back with a real barcode reader, so a style that would fail on a
/// phone fails here first.
///
/// Run this before believing any change to the renderer is safe.
/// </summary>
public class StyleMatrixScanTests
{
    private const string ShortPayload = "https://www.example.org";

    /// <summary>Long enough to force a dense symbol, where thin shapes are most likely to break.</summary>
    private const string LongPayload =
        "https://www.example.org/events/spring-open-day?session=afternoon&year=2026&ref=printed-flyer-lobby";

    public static TheoryData<ModuleShape, MarkerFrameShape, MarkerCenterShape> ShapeCombinations()
    {
        var data = new TheoryData<ModuleShape, MarkerFrameShape, MarkerCenterShape>();

        foreach (var module in Enum.GetValues<ModuleShape>())
        {
            foreach (var frame in Enum.GetValues<MarkerFrameShape>())
            {
                foreach (var center in Enum.GetValues<MarkerCenterShape>())
                {
                    data.Add(module, frame, center);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ShapeCombinations))]
    public void EveryShapeCombination_Scans(ModuleShape module, MarkerFrameShape frame, MarkerCenterShape center)
    {
        var style = QrStyle.Default with
        {
            ModuleShape = module,
            MarkerFrameShape = frame,
            MarkerCenterShape = center,
        };

        AssertScans(style, ShortPayload);
    }

    [Theory]
    [MemberData(nameof(ShapeCombinations))]
    public void EveryShapeCombination_ScansOnADenseSymbol(
        ModuleShape module, MarkerFrameShape frame, MarkerCenterShape center)
    {
        var style = QrStyle.Default with
        {
            ModuleShape = module,
            MarkerFrameShape = frame,
            MarkerCenterShape = center,
        };

        AssertScans(style, LongPayload);
    }

    public static TheoryData<ModuleShape, EccLevel, int> ShapeByEccBySize()
    {
        var data = new TheoryData<ModuleShape, EccLevel, int>();

        foreach (var module in Enum.GetValues<ModuleShape>())
        {
            foreach (var ecc in Enum.GetValues<EccLevel>())
            {
                foreach (var size in new[] { 256, 512, 1024 })
                {
                    data.Add(module, ecc, size);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ShapeByEccBySize))]
    public void EveryShape_ScansAtEveryEccAndExportSize(ModuleShape module, EccLevel ecc, int size)
    {
        var style = QrStyle.Default with { ModuleShape = module, Ecc = ecc, PixelSize = size };

        AssertScans(style, ShortPayload, size);
    }

    [Theory]
    [InlineData(ModuleShape.Square)]
    [InlineData(ModuleShape.RoundedSquare)]
    [InlineData(ModuleShape.Circle)]
    [InlineData(ModuleShape.Diamond)]
    [InlineData(ModuleShape.Fluid)]
    public void EveryShape_ScansWithATransparentBackground(ModuleShape module)
    {
        var style = QrStyle.Default with { ModuleShape = module, Background = null };

        AssertScans(style, ShortPayload);
    }

    [Theory]
    [InlineData(ModuleShape.Square, 0.55)]
    [InlineData(ModuleShape.Square, 1.0)]
    [InlineData(ModuleShape.Circle, 0.55)]
    [InlineData(ModuleShape.Circle, 0.75)]
    [InlineData(ModuleShape.Circle, 1.0)]
    [InlineData(ModuleShape.RoundedSquare, 0.55)]
    [InlineData(ModuleShape.Diamond, 0.75)]
    [InlineData(ModuleShape.Diamond, 1.0)]
    [InlineData(ModuleShape.Fluid, 0.55)]
    [InlineData(ModuleShape.Fluid, 1.0)]
    public void ModuleGap_ScansAcrossTheWholeAllowedRange(ModuleShape module, double scale)
    {
        var style = QrStyle.Default with { ModuleShape = module, ModuleScale = scale };

        AssertScans(style, ShortPayload);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void QuietZone_ScansAtEverySensibleMargin(int quietZone)
    {
        AssertScans(QrStyle.Default with { QuietZoneModules = quietZone }, ShortPayload);
    }

    [Theory]
    [InlineData(OutlineTarget.Modules)]
    [InlineData(OutlineTarget.Markers)]
    [InlineData(OutlineTarget.Both)]
    public void Outline_ScansForEveryTarget(OutlineTarget target)
    {
        var style = QrStyle.Default with
        {
            ModuleShape = ModuleShape.RoundedSquare,
            ModuleScale = 0.85,
            Outline = new OutlineStyle
            {
                Enabled = true,
                Color = Colors.White,
                ThicknessRatio = 0.08,
                Target = target,
            },
        };

        AssertScans(style, ShortPayload);
    }

    [Fact]
    public void DistinctMarkerColours_StillScan()
    {
        var style = QrStyle.Default with
        {
            Foreground = Color.FromRgb(0x1A, 0x1A, 0x2E),
            MarkerFrameColor = Color.FromRgb(0x8B, 0x1A, 0x2B),
            MarkerCenterColor = Color.FromRgb(0x1A, 0x1A, 0x2E),
            ModuleShape = ModuleShape.Fluid,
            MarkerFrameShape = MarkerFrameShape.RoundedSquare,
            MarkerCenterShape = MarkerCenterShape.Circle,
        };

        AssertScans(style, ShortPayload);
    }

    [Fact]
    public void TheBuiltInPresets_AllScan()
    {
        foreach (var preset in StylePresets.BuiltIn)
        {
            AssertScans(preset.Style, ShortPayload, because: preset.Name);
            AssertScans(preset.Style, LongPayload, because: preset.Name);
        }
    }

    /// <summary>
    /// Renders the style and reads it back, failing with enough detail to identify the
    /// offending combination without rerunning the suite.
    /// </summary>
    private static void AssertScans(QrStyle style, string payload, int? pixelSize = null, string? because = null)
    {
        var size = pixelSize ?? 512;

        var decoded = StaThread.Run(() =>
        {
            var matrix = QrEncoder.Encode(payload, style.Ecc).Matrix!;
            var drawing = QrGeometryBuilder.Build(matrix, style);
            var bitmap = QrRenderer.RenderToBitmap(drawing, size, Colors.White);

            // Deliberately the strict, camera-like pass. The app itself is more forgiving,
            // because that pass has a measurable false-failure rate on clean renders and
            // must not tell a user their good code is broken. A style shipped in the app
            // is held to the higher bar instead.
            return QrDecoder.DecodeStrict(bitmap);
        });

        var label = because is null ? string.Empty : $"[{because}] ";
        Assert.True(
            decoded == payload,
            $"{label}did not scan: module={style.ModuleShape}, frame={style.MarkerFrameShape}, " +
            $"center={style.MarkerCenterShape}, scale={style.ModuleScale}, quiet={style.QuietZoneModules}, " +
            $"ecc={style.Ecc}, size={size}px, outline={style.Outline.Enabled}. Decoded: {decoded ?? "(nothing)"}");
    }
}
