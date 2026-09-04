# Cross-platform port, Phase 1: Core loses WPF

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove every WPF dependency from `TrispotQR.Core` so it targets plain `net10.0`, rasterising with SkiaSharp, while the existing WPF app keeps running on top of it and the whole test suite stays green.

**Architecture:** Core gains two primitives, `RgbColor` and `QrPath`, and stops speaking WPF's `Color` and `Geometry`. `QrPath` is the single geometry model in module units; it converts to an `SKPath` for rasterising, to SVG path data for export, and (temporarily, from the app only) to a WPF `Geometry` for the existing preview. Rasterising moves to SkiaSharp, which draws offscreen with no window and behaves identically on every platform. Nothing about the UI changes in this phase.

**Tech Stack:** .NET 10, SkiaSharp 4.151.2, ZXing.Net 0.16.11, Net.Codecrete.QrCodeGenerator 3.1.0, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-04-cross-platform-port-design.md`

## Global Constraints

- **Core must not reference WPF.** No `System.Windows`, no `UseWPF`, no `net10.0-windows`. Task 12 flips the target framework, and that flip is the proof.
- **Behaviour is preserved.** This phase changes no user-visible behaviour. Any difference is a bug, except antialiasing, where pixel-identical output is explicitly not the goal and decoding correctly is.
- **Every task leaves a compiling tree and a green suite.** Never commit red.
- **The preset file format does not change.** Colours are stored as `#RRGGBB`, or `#AARRGGBB` when alpha is below 255. Existing `presets.json` and `settings.json` must keep loading.
- **`System.IO` is NOT in the implicit usings while Core is still a `UseWPF` project.** Add `using System.IO;` explicitly in any Core file that touches the filesystem, until Task 12.
- **Commit style:** author is `levinium`, no `Co-Authored-By` trailer, no mention of Claude.
- **Geometry is in module units** with the quiet zone included. Pixel size is applied by a single scale at render time.
- **Line endings are LF**, enforced by `.gitattributes`.
- **Mobile rules from the spec apply now:** Core takes bytes and streams, never paths or dialogs; storage goes behind an interface; no desktop assumptions in Core.

## Existing API this plan must respect

Verified against the tree before writing. Do not invent alternatives.

```csharp
// Styling/QrStyle.cs
OutlineStyle { bool Enabled; Color Color; double ThicknessRatio; OutlineTarget Target; }
LogoStyle    { string? Path; double SizeRatio; LogoPunchShape PunchShape; double PunchPadding; bool HasImage; }
QrStyle      { ... Color Foreground; Color? Background; Color? MarkerFrameColor; Color? MarkerCenterColor;
               Color EffectiveMarkerFrameColor; Color EffectiveMarkerCenterColor; }

// Styling/Shapes.cs
enum OutlineTarget   { Modules, Markers, Both }
enum LogoPunchShape  { None, Circle, RoundedSquare }
```

The outline predicate, currently inside `QrGeometryBuilder.PenFor`, is exactly:

```csharp
var applies = outline.Target == OutlineTarget.Both || outline.Target == part;
```

---

## File Structure

**New in `src/TrispotQR.Core/`:**

| File | Responsibility |
|---|---|
| `Primitives/RgbColor.cs` | Colour value type replacing `System.Windows.Media.Color` |
| `Primitives/QrPath.cs` | Points, segments, figures, fill rule |
| `Primitives/QrPathBuilder.cs` | Accumulates figures into a `QrPath` |
| `Rendering/SkiaPath.cs` | `QrPath` to `SKPath` and back |
| `Rendering/SkiaPathOps.cs` | Boolean exclude, for the logo punch-out |
| `Rendering/ImageSize.cs` | Pixel dimensions of an image file, without decoding it |
| `Rendering/SkiaRasterizer.cs` | `QrDrawing` to pixels. No window, no display |
| `Export/SvgPathData.cs` | `QrPath` to an SVG `d` attribute |
| `Export/IImageClipboard.cs` | Clipboard behind an interface |
| `Presets/ISettingsLocation.cs`, `Presets/DesktopSettingsLocation.cs` | Where settings live |

**Rewritten in Core:** `Rendering/ShapeFactory.cs`, `Rendering/QrDrawing.cs`, `Rendering/QrGeometryBuilder.cs`, `Rendering/LogoCompositor.cs`, `Export/PngExporter.cs`, `Export/SvgExporter.cs`, `Validation/QrDecoder.cs`, `Validation/ScannabilityChecker.cs`, `Presets/ColorJsonConverter.cs`, `Presets/PresetStore.cs`, `Styling/QrStyle.cs`, `Styling/StylePresets.cs`, `Styling/HsvColor.cs`.

**Deleted from Core:** `Rendering/QrRenderer.cs` (moves to the app), `Rendering/StaThread.cs`, `Export/ClipboardExporter.cs` (moves to the app).

**New in `src/TrispotQR.App/`:** `Rendering/WpfGeometryAdapter.cs`, `Rendering/WpfQrRenderer.cs` (both **throwaway**, deleted in Phase 2), `Export/WpfImageClipboard.cs`.

---

## Task 1: RgbColor

**Files:**
- Create: `src/TrispotQR.Core/Primitives/RgbColor.cs`
- Test: `tests/TrispotQR.Tests/RgbColorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `readonly record struct RgbColor(byte A, byte R, byte G, byte B)`; statics `FromRgb`, `FromArgb`, `Black`, `White`, `Transparent`; `bool IsTransparent`; `string ToHex()`; `static bool TryParse(string?, out RgbColor)`; `static RgbColor Parse(string)`.

- [ ] **Step 1: Write the failing test**

```csharp
using TrispotQR.Core.Primitives;

namespace TrispotQR.Tests;

public class RgbColorTests
{
    [Fact]
    public void FromRgb_IsFullyOpaque() => Assert.Equal(255, RgbColor.FromRgb(1, 2, 3).A);

    [Fact]
    public void ToHex_OmitsAlphaWhenOpaque() =>
        Assert.Equal("#1B2A4A", RgbColor.FromRgb(0x1B, 0x2A, 0x4A).ToHex());

    [Fact]
    public void ToHex_IncludesAlphaWhenNotOpaque() =>
        Assert.Equal("#801B2A4A", RgbColor.FromArgb(0x80, 0x1B, 0x2A, 0x4A).ToHex());

    [Theory]
    [InlineData("#1B2A4A", 255, 0x1B, 0x2A, 0x4A)]
    [InlineData("#801B2A4A", 0x80, 0x1B, 0x2A, 0x4A)]
    [InlineData("  #1b2a4a  ", 255, 0x1B, 0x2A, 0x4A)]
    public void TryParse_ReadsTheFormatsWeWrite(string text, int a, int r, int g, int b)
    {
        Assert.True(RgbColor.TryParse(text, out var colour));
        Assert.Equal(new RgbColor((byte)a, (byte)r, (byte)g, (byte)b), colour);
    }

    /// <summary>A hand-edited preset file may carry a WPF colour name.</summary>
    [Theory]
    [InlineData("Black", 0, 0, 0)]
    [InlineData("White", 255, 255, 255)]
    public void TryParse_StillReadsTheCommonNames(string text, int r, int g, int b)
    {
        Assert.True(RgbColor.TryParse(text, out var colour));
        Assert.Equal(RgbColor.FromRgb((byte)r, (byte)g, (byte)b), colour);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nonsense")]
    [InlineData("#12345")]
    public void TryParse_RejectsTheRest(string? text) => Assert.False(RgbColor.TryParse(text, out _));

    [Fact]
    public void RoundTrip_SurvivesHex()
    {
        var original = RgbColor.FromArgb(0x7F, 0x10, 0x20, 0x30);
        Assert.True(RgbColor.TryParse(original.ToHex(), out var parsed));
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Transparent_KnowsItIs()
    {
        Assert.True(RgbColor.Transparent.IsTransparent);
        Assert.False(RgbColor.Black.IsTransparent);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~RgbColorTests"`
Expected: FAIL to compile, "The type or namespace name 'RgbColor' could not be found".

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Globalization;

namespace TrispotQR.Core.Primitives;

/// <summary>
/// A colour, with no dependency on any UI framework.
///
/// This replaces System.Windows.Media.Color, the single thing that kept most of this
/// project's styling code tied to Windows. Channel order matches the old type (A, R, G, B)
/// so the swap reads the same at every call site.
/// </summary>
public readonly record struct RgbColor(byte A, byte R, byte G, byte B)
{
    public static readonly RgbColor Black = FromRgb(0, 0, 0);
    public static readonly RgbColor White = FromRgb(255, 255, 255);
    public static readonly RgbColor Transparent = FromArgb(0, 255, 255, 255);

    /// <summary>The handful of names a hand-edited preset file might reasonably contain.</summary>
    private static readonly Dictionary<string, RgbColor> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = Black,
        ["white"] = White,
        ["transparent"] = Transparent,
        ["red"] = FromRgb(255, 0, 0),
        ["green"] = FromRgb(0, 128, 0),
        ["blue"] = FromRgb(0, 0, 255),
        ["gray"] = FromRgb(128, 128, 128),
        ["grey"] = FromRgb(128, 128, 128),
    };

    public static RgbColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    public static RgbColor FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);

    /// <summary>True when this colour would paint nothing at all.</summary>
    public bool IsTransparent => A == 0;

    /// <summary>
    /// The format the preset file has always used: alpha is written only when it is not
    /// fully opaque, so the common case stays a readable six-digit hex.
    /// </summary>
    public string ToHex() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    public static RgbColor Parse(string text) =>
        TryParse(text, out var colour) ? colour : throw new FormatException($"\"{text}\" is not a colour.");

    public static bool TryParse(string? text, out RgbColor colour)
    {
        colour = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        if (Named.TryGetValue(trimmed, out colour))
        {
            return true;
        }

        if (trimmed[0] != '#')
        {
            return false;
        }

        var digits = trimmed[1..];

        if (!uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        switch (digits.Length)
        {
            case 6:
                colour = FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
                return true;
            case 8:
                colour = FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
                return true;
            default:
                return false;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~RgbColorTests"`
Expected: PASS, 16 tests.

- [ ] **Step 5: Commit**

```bash
git add src/TrispotQR.Core/Primitives/RgbColor.cs tests/TrispotQR.Tests/RgbColorTests.cs
git commit -m "Add RgbColor, a colour type with no UI framework behind it"
```

---

## Task 2: The QrPath geometry model

**Files:**
- Create: `src/TrispotQR.Core/Primitives/QrPath.cs`, `src/TrispotQR.Core/Primitives/QrPathBuilder.cs`
- Test: `tests/TrispotQR.Tests/QrPathTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `readonly record struct QrPoint(double X, double Y)`; `abstract record QrSegment` with `QrLineTo(QrPoint To)`, `QrArcTo(QrPoint To, double Radius, bool Clockwise)`, `QrCubicTo(QrPoint C1, QrPoint C2, QrPoint To)`; `sealed record QrFigure(QrPoint Start, IReadOnlyList<QrSegment> Segments, bool IsClosed)`; `enum QrFillRule { NonZero, EvenOdd }`; `sealed record QrPath(IReadOnlyList<QrFigure> Figures, QrFillRule FillRule)` with `static QrPath Empty` and `bool IsEmpty`; `sealed class QrPathBuilder` with `Add`, `AddRange`, `Build(QrFillRule)`.

- [ ] **Step 1: Write the failing test**

```csharp
using TrispotQR.Core.Primitives;

namespace TrispotQR.Tests;

public class QrPathTests
{
    private static QrFigure Square(double x, double y, double size) =>
        new(new QrPoint(x, y),
        [
            new QrLineTo(new QrPoint(x + size, y)),
            new QrLineTo(new QrPoint(x + size, y + size)),
            new QrLineTo(new QrPoint(x, y + size)),
        ],
        IsClosed: true);

    [Fact]
    public void Empty_HasNoFigures()
    {
        Assert.True(QrPath.Empty.IsEmpty);
        Assert.Empty(QrPath.Empty.Figures);
    }

    [Fact]
    public void Builder_CollectsFiguresInOrder()
    {
        var path = new QrPathBuilder().Add(Square(0, 0, 1)).Add(Square(2, 0, 1)).Build(QrFillRule.NonZero);

        Assert.Equal(2, path.Figures.Count);
        Assert.Equal(new QrPoint(0, 0), path.Figures[0].Start);
        Assert.Equal(new QrPoint(2, 0), path.Figures[1].Start);
        Assert.Equal(QrFillRule.NonZero, path.FillRule);
        Assert.False(path.IsEmpty);
    }

    [Fact]
    public void Builder_CarriesTheFillRule() =>
        Assert.Equal(QrFillRule.EvenOdd, new QrPathBuilder().Build(QrFillRule.EvenOdd).FillRule);

    [Fact]
    public void Builder_AddRangeAppends()
    {
        var path = new QrPathBuilder().AddRange([Square(0, 0, 1), Square(1, 0, 1)]).Build(QrFillRule.NonZero);
        Assert.Equal(2, path.Figures.Count);
    }

    /// <summary>Segments compare by value, which lets tests assert a shape without reaching into it.</summary>
    [Fact]
    public void Segments_CompareByValue()
    {
        Assert.Equal(new QrLineTo(new QrPoint(1, 2)), new QrLineTo(new QrPoint(1, 2)));
        Assert.NotEqual<QrSegment>(new QrLineTo(new QrPoint(1, 2)), new QrLineTo(new QrPoint(1, 3)));
    }

    [Fact]
    public void Figures_KnowWhetherTheyClose()
    {
        Assert.True(Square(0, 0, 1).IsClosed);
        Assert.False(new QrFigure(new QrPoint(0, 0), [new QrLineTo(new QrPoint(1, 1))], IsClosed: false).IsClosed);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~QrPathTests"`
Expected: FAIL to compile, "The type or namespace name 'QrPoint' could not be found".

- [ ] **Step 3: Write minimal implementation**

`src/TrispotQR.Core/Primitives/QrPath.cs`:

```csharp
namespace TrispotQR.Core.Primitives;

/// <summary>A point in module units.</summary>
public readonly record struct QrPoint(double X, double Y);

/// <summary>How overlapping parts of a path are filled.</summary>
public enum QrFillRule
{
    NonZero,
    EvenOdd,
}

/// <summary>One step along a figure, starting wherever the previous step ended.</summary>
public abstract record QrSegment;

/// <summary>A straight line.</summary>
public sealed record QrLineTo(QrPoint To) : QrSegment;

/// <summary>
/// A circular arc. Always the small arc: every corner this app draws is a quarter circle
/// or less, so there is no large-arc flag to carry around.
/// </summary>
public sealed record QrArcTo(QrPoint To, double Radius, bool Clockwise) : QrSegment;

/// <summary>A cubic bezier. Not produced by ShapeFactory; needed because Skia emits them.</summary>
public sealed record QrCubicTo(QrPoint C1, QrPoint C2, QrPoint To) : QrSegment;

/// <summary>One continuous outline.</summary>
public sealed record QrFigure(QrPoint Start, IReadOnlyList<QrSegment> Segments, bool IsClosed);

/// <summary>
/// The geometry model everything is drawn from, in module units.
///
/// This is the single source the preview, the PNG rasteriser and the SVG writer all derive
/// from, so none of them can drift from the others. It deliberately owes nothing to any UI
/// framework: that is what lets Core target plain .NET and run on macOS, Linux and mobile.
/// </summary>
public sealed record QrPath(IReadOnlyList<QrFigure> Figures, QrFillRule FillRule)
{
    public static readonly QrPath Empty = new([], QrFillRule.NonZero);

    public bool IsEmpty => Figures.Count == 0;
}
```

`src/TrispotQR.Core/Primitives/QrPathBuilder.cs`:

```csharp
namespace TrispotQR.Core.Primitives;

/// <summary>Accumulates figures, then hands back an immutable <see cref="QrPath"/>.</summary>
public sealed class QrPathBuilder
{
    private readonly List<QrFigure> _figures = [];

    public QrPathBuilder Add(QrFigure figure)
    {
        _figures.Add(figure);
        return this;
    }

    public QrPathBuilder AddRange(IEnumerable<QrFigure> figures)
    {
        _figures.AddRange(figures);
        return this;
    }

    public QrPath Build(QrFillRule fillRule) => new([.. _figures], fillRule);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~QrPathTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/TrispotQR.Core/Primitives/QrPath.cs src/TrispotQR.Core/Primitives/QrPathBuilder.cs tests/TrispotQR.Tests/QrPathTests.cs
git commit -m "Add QrPath, the geometry model that replaces WPF Geometry"
```

---

## Task 3: QrPath to SKPath and back

Deliberately before the `ShapeFactory` change: this needs only `QrPath`, so its tests build
geometry from `QrFigure` literals and the tree keeps compiling.

**Files:**
- Modify: `src/TrispotQR.Core/TrispotQR.Core.csproj` (add SkiaSharp)
- Create: `src/TrispotQR.Core/Rendering/SkiaPath.cs`
- Test: `tests/TrispotQR.Tests/SkiaPathTests.cs`

**Interfaces:**
- Consumes: `QrPath` and its segment records from Task 2.
- Produces: `internal static class SkiaPath` with `SKPath ToSKPath(QrPath)` and `QrPath ToQrPath(SKPath, QrFillRule)`.

- [ ] **Step 1: Write the failing test**

```csharp
using SkiaSharp;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Rendering;

namespace TrispotQR.Tests;

public class SkiaPathTests
{
    /// <summary>
    /// Built by hand rather than through ShapeFactory: this task runs before ShapeFactory
    /// changes, and depending on it would couple two independent pieces of work.
    /// </summary>
    private static QrPath Square(double x, double y, double size, QrFillRule rule = QrFillRule.NonZero) =>
        new QrPathBuilder()
            .Add(new QrFigure(
                new QrPoint(x, y),
                [
                    new QrLineTo(new QrPoint(x + size, y)),
                    new QrLineTo(new QrPoint(x + size, y + size)),
                    new QrLineTo(new QrPoint(x, y + size)),
                ],
                IsClosed: true))
            .Build(rule);

    [Fact]
    public void ASquare_KeepsItsBoundsThroughSkia()
    {
        using var sk = SkiaPath.ToSKPath(Square(1, 2, 10));

        Assert.Equal(1f, sk.Bounds.Left, 3);
        Assert.Equal(2f, sk.Bounds.Top, 3);
        Assert.Equal(11f, sk.Bounds.Right, 3);
        Assert.Equal(12f, sk.Bounds.Bottom, 3);
    }

    [Fact]
    public void FillRule_IsCarriedAcross()
    {
        using var nonZero = SkiaPath.ToSKPath(Square(0, 0, 1));
        using var evenOdd = SkiaPath.ToSKPath(Square(0, 0, 1, QrFillRule.EvenOdd));

        Assert.Equal(SKPathFillType.Winding, nonZero.FillType);
        Assert.Equal(SKPathFillType.EvenOdd, evenOdd.FillType);
    }

    [Fact]
    public void AnArc_IsDrawnAsAnArc()
    {
        var path = new QrPathBuilder()
            .Add(new QrFigure(
                new QrPoint(0, 2),
                [new QrArcTo(new QrPoint(2, 0), 2, Clockwise: true)],
                IsClosed: false))
            .Build(QrFillRule.NonZero);

        using var sk = SkiaPath.ToSKPath(path);

        Assert.Equal(0f, sk.Bounds.Left, 2);
        Assert.Equal(2f, sk.Bounds.Right, 2);
        Assert.False(sk.IsEmpty);
    }

    /// <summary>
    /// The reverse trip exists for the logo punch-out, which is a Skia boolean op whose
    /// result has to come back into the model so SVG export sees the same shape.
    /// </summary>
    [Fact]
    public void RoundTrip_PreservesBounds()
    {
        var original = Square(3, 4, 6);
        using var sk = SkiaPath.ToSKPath(original);
        var back = SkiaPath.ToQrPath(sk, QrFillRule.NonZero);

        Assert.NotEmpty(back.Figures);

        using var again = SkiaPath.ToSKPath(back);
        Assert.Equal(sk.Bounds.Left, again.Bounds.Left, 3);
        Assert.Equal(sk.Bounds.Right, again.Bounds.Right, 3);
        Assert.Equal(sk.Bounds.Bottom, again.Bounds.Bottom, 3);
    }

    [Fact]
    public void RoundTrip_KeepsTheFigureClosed()
    {
        using var sk = SkiaPath.ToSKPath(Square(0, 0, 1));
        var back = SkiaPath.ToQrPath(sk, QrFillRule.NonZero);

        Assert.True(back.Figures[0].IsClosed);
    }

    [Fact]
    public void AnEmptyPath_MakesAnEmptySKPath()
    {
        using var sk = SkiaPath.ToSKPath(QrPath.Empty);
        Assert.True(sk.IsEmpty);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~SkiaPathTests"`
Expected: FAIL to compile, "The type or namespace name 'SkiaSharp' could not be found".

- [ ] **Step 3: Write minimal implementation**

Add inside the existing `PackageReference` `ItemGroup` of `src/TrispotQR.Core/TrispotQR.Core.csproj`:

```xml
    <PackageReference Include="SkiaSharp" Version="4.151.2" />
    <!--
      Native binaries are per-platform and the main package brings in only the host's.
      Naming all three keeps a self-contained publish working for any RID, which Phase 3
      depends on.
    -->
    <PackageReference Include="SkiaSharp.NativeAssets.Win32" Version="4.151.2" />
    <PackageReference Include="SkiaSharp.NativeAssets.macOS" Version="4.151.2" />
    <PackageReference Include="SkiaSharp.NativeAssets.Linux" Version="4.151.2" />
```

Create `src/TrispotQR.Core/Rendering/SkiaPath.cs`:

```csharp
using SkiaSharp;
using TrispotQR.Core.Primitives;

namespace TrispotQR.Core.Rendering;

/// <summary>
/// Converts between the app's own geometry model and Skia's.
///
/// The reverse direction exists for one reason: the logo punch-out is a boolean operation
/// only Skia can do, and its result has to come back into <see cref="QrPath"/> so the SVG
/// writer serialises exactly the shape the rasteriser paints.
/// </summary>
internal static class SkiaPath
{
    public static SKPath ToSKPath(QrPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var result = new SKPath
        {
            FillType = path.FillRule == QrFillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding,
        };

        foreach (var figure in path.Figures)
        {
            result.MoveTo((float)figure.Start.X, (float)figure.Start.Y);

            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case QrLineTo line:
                        result.LineTo((float)line.To.X, (float)line.To.Y);
                        break;

                    case QrArcTo arc:
                        result.ArcTo(
                            new SKPoint((float)arc.Radius, (float)arc.Radius),
                            0,
                            SKPathArcSize.Small,
                            arc.Clockwise ? SKPathDirection.Clockwise : SKPathDirection.CounterClockwise,
                            new SKPoint((float)arc.To.X, (float)arc.To.Y));
                        break;

                    case QrCubicTo cubic:
                        result.CubicTo(
                            (float)cubic.C1.X, (float)cubic.C1.Y,
                            (float)cubic.C2.X, (float)cubic.C2.Y,
                            (float)cubic.To.X, (float)cubic.To.Y);
                        break;
                }
            }

            if (figure.IsClosed)
            {
                result.Close();
            }
        }

        return result;
    }

    /// <summary>
    /// Reads a Skia path back into the model. Skia has already turned arcs into conics or
    /// cubics by this point, which is why <see cref="QrCubicTo"/> exists: the model has to
    /// express whatever comes back, or the punch-out would lose its rounded corners.
    /// </summary>
    public static QrPath ToQrPath(SKPath path, QrFillRule fillRule)
    {
        ArgumentNullException.ThrowIfNull(path);

        var figures = new List<QrFigure>();
        var segments = new List<QrSegment>();
        var start = new QrPoint();
        var open = false;

        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];

        while (true)
        {
            var verb = iterator.Next(points);

            if (verb == SKPathVerb.Done)
            {
                break;
            }

            switch (verb)
            {
                case SKPathVerb.Move:
                    Flush(figures, ref segments, start, open, closed: false);
                    start = Point(points[0]);
                    open = true;
                    break;

                case SKPathVerb.Line:
                    segments.Add(new QrLineTo(Point(points[1])));
                    break;

                case SKPathVerb.Quad:
                case SKPathVerb.Conic:
                    // Raised to a cubic so the model needs only one curve type. A conic is
                    // approximated; a quadratic converts exactly.
                    segments.Add(QuadToCubic(Point(points[0]), Point(points[1]), Point(points[2])));
                    break;

                case SKPathVerb.Cubic:
                    segments.Add(new QrCubicTo(Point(points[1]), Point(points[2]), Point(points[3])));
                    break;

                case SKPathVerb.Close:
                    Flush(figures, ref segments, start, open, closed: true);
                    open = false;
                    break;
            }
        }

        Flush(figures, ref segments, start, open, closed: false);
        return new QrPath(figures, fillRule);
    }

    private static void Flush(
        List<QrFigure> figures, ref List<QrSegment> segments, QrPoint start, bool open, bool closed)
    {
        if (!open || segments.Count == 0)
        {
            segments = [];
            return;
        }

        figures.Add(new QrFigure(start, segments, closed));
        segments = [];
    }

    /// <summary>A quadratic raised to an equivalent cubic, which is exact rather than approximate.</summary>
    private static QrCubicTo QuadToCubic(QrPoint from, QrPoint control, QrPoint to) =>
        new(
            new QrPoint(from.X + (2.0 / 3.0 * (control.X - from.X)), from.Y + (2.0 / 3.0 * (control.Y - from.Y))),
            new QrPoint(to.X + (2.0 / 3.0 * (control.X - to.X)), to.Y + (2.0 / 3.0 * (control.Y - to.Y))),
            to);

    private static QrPoint Point(SKPoint p) => new(p.X, p.Y);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~SkiaPathTests"`
Expected: PASS, 6 tests. The rest of the suite must still pass too; nothing else changed.

- [ ] **Step 5: Commit**

```bash
git add src/TrispotQR.Core/TrispotQR.Core.csproj src/TrispotQR.Core/Rendering/SkiaPath.cs tests/TrispotQR.Tests/SkiaPathTests.cs
git commit -m "Convert between QrPath and SKPath"
```

---

## Task 4: SVG path data from QrPath

Also before the `ShapeFactory` change, for the same reason. Tests build geometry by hand.

**Files:**
- Create: `src/TrispotQR.Core/Export/SvgPathData.cs`
- Test: `tests/TrispotQR.Tests/SvgPathDataTests.cs`

**Interfaces:**
- Consumes: `QrPath` and its segment records.
- Produces: `internal static class SvgPathData` with `string ToData(QrPath)` and `string FillRule(QrFillRule)`.

This replaces stripping WPF's `F0`/`F1` prefix off `Geometry.ToString()`, a WPF-specific
debug-string behaviour that would not have survived the port.

- [ ] **Step 1: Write the failing test**

```csharp
using TrispotQR.Core.Export;
using TrispotQR.Core.Primitives;

namespace TrispotQR.Tests;

public class SvgPathDataTests
{
    private static QrPath Of(QrFigure figure) => new QrPathBuilder().Add(figure).Build(QrFillRule.NonZero);

    [Fact]
    public void AClosedSquare_IsMoveLinesAndZ()
    {
        var figure = new QrFigure(
            new QrPoint(0, 0),
            [
                new QrLineTo(new QrPoint(2, 0)),
                new QrLineTo(new QrPoint(2, 2)),
                new QrLineTo(new QrPoint(0, 2)),
            ],
            IsClosed: true);

        Assert.Equal("M0,0 L2,0 L2,2 L0,2 Z", SvgPathData.ToData(Of(figure)));
    }

    [Fact]
    public void AnArc_UsesTheSvgArcCommand()
    {
        var figure = new QrFigure(
            new QrPoint(0, 2),
            [new QrArcTo(new QrPoint(2, 0), 2, Clockwise: true)],
            IsClosed: false);

        // rx,ry rotation large-arc sweep x,y
        Assert.Equal("M0,2 A2,2 0 0 1 2,0", SvgPathData.ToData(Of(figure)));
    }

    [Fact]
    public void ACounterClockwiseArc_FlipsTheSweepFlag()
    {
        var figure = new QrFigure(
            new QrPoint(0, 2),
            [new QrArcTo(new QrPoint(2, 0), 2, Clockwise: false)],
            IsClosed: false);

        Assert.Contains(" 0 0 0 ", SvgPathData.ToData(Of(figure)));
    }

    [Fact]
    public void ACubic_UsesTheCurveCommand()
    {
        var figure = new QrFigure(
            new QrPoint(0, 0),
            [new QrCubicTo(new QrPoint(1, 0), new QrPoint(2, 1), new QrPoint(2, 2))],
            IsClosed: false);

        Assert.Equal("M0,0 C1,0 2,1 2,2", SvgPathData.ToData(Of(figure)));
    }

    [Fact]
    public void Numbers_AreInvariantAndTrimmed()
    {
        var figure = new QrFigure(
            new QrPoint(1.5, 2.25), [new QrLineTo(new QrPoint(3.0, 4.123456))], IsClosed: false);

        Assert.Equal("M1.5,2.25 L3,4.1235", SvgPathData.ToData(Of(figure)));
    }

    [Fact]
    public void AnEmptyPath_ProducesNothing() => Assert.Equal(string.Empty, SvgPathData.ToData(QrPath.Empty));

    [Fact]
    public void MultipleFigures_EachStartWithAMove()
    {
        var one = new QrFigure(new QrPoint(0, 0), [new QrLineTo(new QrPoint(1, 0))], IsClosed: true);
        var two = new QrFigure(new QrPoint(5, 5), [new QrLineTo(new QrPoint(6, 5))], IsClosed: true);
        var data = SvgPathData.ToData(new QrPathBuilder().Add(one).Add(two).Build(QrFillRule.NonZero));

        Assert.Equal("M0,0 L1,0 Z M5,5 L6,5 Z", data);
    }

    [Fact]
    public void FillRule_MapsToTheSvgKeywords()
    {
        Assert.Equal("nonzero", SvgPathData.FillRule(QrFillRule.NonZero));
        Assert.Equal("evenodd", SvgPathData.FillRule(QrFillRule.EvenOdd));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~SvgPathDataTests"`
Expected: FAIL to compile, "The name 'SvgPathData' does not exist".

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Globalization;
using System.Text;
using TrispotQR.Core.Primitives;

namespace TrispotQR.Core.Export;

/// <summary>
/// Serialises a <see cref="QrPath"/> to the SVG <c>d</c> attribute.
///
/// The previous version took WPF's path mini-language from Geometry.ToString and stripped
/// its leading fill-rule token. That worked, and it depended on the debug-string format of
/// a Windows-only type. Writing the data is a dozen lines and owes nothing to anything.
/// </summary>
internal static class SvgPathData
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string FillRule(QrFillRule rule) => rule == QrFillRule.EvenOdd ? "evenodd" : "nonzero";

    public static string ToData(QrPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var builder = new StringBuilder();

        foreach (var figure in path.Figures)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append('M').Append(Pair(figure.Start));

            foreach (var segment in figure.Segments)
            {
                builder.Append(' ');

                switch (segment)
                {
                    case QrLineTo line:
                        builder.Append('L').Append(Pair(line.To));
                        break;

                    case QrArcTo arc:
                        builder.Append('A')
                            .Append(Num(arc.Radius)).Append(',').Append(Num(arc.Radius))
                            .Append(" 0 0 ")
                            .Append(arc.Clockwise ? '1' : '0')
                            .Append(' ')
                            .Append(Pair(arc.To));
                        break;

                    case QrCubicTo cubic:
                        builder.Append('C')
                            .Append(Pair(cubic.C1)).Append(' ')
                            .Append(Pair(cubic.C2)).Append(' ')
                            .Append(Pair(cubic.To));
                        break;
                }
            }

            if (figure.IsClosed)
            {
                builder.Append(" Z");
            }
        }

        return builder.ToString();
    }

    private static string Pair(QrPoint point) => $"{Num(point.X)},{Num(point.Y)}";

    private static string Num(double value) => value.ToString("0.####", Invariant);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~SvgPathDataTests"`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/TrispotQR.Core/Export/SvgPathData.cs tests/TrispotQR.Tests/SvgPathDataTests.cs
git commit -m "Write SVG path data from the geometry model"
```

---

## Task 5: The switch

The largest task, and necessarily atomic: geometry and colour both run through
`QrDrawing`, so the tree cannot compile with only one of them changed. Ends green.

**Files:**
- Modify: `src/TrispotQR.Core/Rendering/ShapeFactory.cs`, `Rendering/QrDrawing.cs`, `Rendering/QrGeometryBuilder.cs`, `Rendering/LogoCompositor.cs`
- Create: `src/TrispotQR.Core/Rendering/SkiaPathOps.cs`, `src/TrispotQR.Core/Rendering/ImageSize.cs`
- Modify: `src/TrispotQR.Core/Styling/QrStyle.cs`, `Styling/StylePresets.cs`, `Styling/HsvColor.cs`, `Presets/ColorJsonConverter.cs`
- Move: `src/TrispotQR.Core/Rendering/QrRenderer.cs` → `src/TrispotQR.App/Rendering/WpfQrRenderer.cs`
- Create: `src/TrispotQR.App/Rendering/WpfGeometryAdapter.cs`
- Modify: every app and test file naming `QrRenderer` or a style `Color`
- Test: `tests/TrispotQR.Tests/SkiaPathOpsTests.cs`, `tests/TrispotQR.Tests/ShapeFactoryTests.cs`

**Interfaces:**
- Consumes: `RgbColor`, `QrPath`, `SkiaPath`.
- Produces:
  - `internal static class ShapeFactory` returning `QrFigure` from the same four signatures
  - `sealed record QrStroke(RgbColor Color, double Thickness)`
  - `sealed class QrLayer(string name, QrPath path, RgbColor fill, QrStroke? stroke)` with `Name`, `Path`, `Fill`, `Stroke`
  - `sealed class QrDrawing(double sizeInUnits, RgbColor? background, IReadOnlyList<QrLayer> layers, LogoPlacement? logo)`
  - `internal static class SkiaPathOps` with `QrPath Exclude(QrPath subject, QrPath punch)`
  - `internal static class ImageSize` with `(int Width, int Height)? Read(string path)`
  - `LogoCompositor.Punch` returning `QrPath?`
  - App-side `WpfGeometryAdapter.ToColor/ToBrush/ToPen/ToGeometry` and `WpfQrRenderer.RenderToVisual/RenderToDrawingImage/RenderToBitmap`, each taking `RgbColor?` where the old signature took `Color?`

- [ ] **Step 1: Write the failing tests**

`tests/TrispotQR.Tests/SkiaPathOpsTests.cs`:

```csharp
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Rendering;

namespace TrispotQR.Tests;

public class SkiaPathOpsTests
{
    private static QrPath Square(double x, double y, double size) =>
        new QrPathBuilder().Add(ShapeFactory.RoundedRect(x, y, size, 0)).Build(QrFillRule.NonZero);

    /// <summary>
    /// The logo punch-out. A hole in the middle of a shape is the whole point, and it has
    /// to come back as a QrPath so the SVG export shows the same hole the PNG does.
    /// </summary>
    [Fact]
    public void Exclude_CutsAHoleAndKeepsTheOutline()
    {
        var result = SkiaPathOps.Exclude(Square(0, 0, 10), Square(4, 4, 2));

        using var sk = SkiaPath.ToSKPath(result);
        Assert.Equal(0f, sk.Bounds.Left, 3);
        Assert.Equal(10f, sk.Bounds.Right, 3);
        Assert.False(sk.Contains(5f, 5f));
        Assert.True(sk.Contains(1f, 1f));
    }

    [Fact]
    public void Exclude_LeavesTheSubjectAloneWhenNothingOverlaps()
    {
        var result = SkiaPathOps.Exclude(Square(0, 0, 2), Square(50, 50, 2));

        using var sk = SkiaPath.ToSKPath(result);
        Assert.True(sk.Contains(1f, 1f));
        Assert.Equal(2f, sk.Bounds.Right, 3);
    }

    [Fact]
    public void Exclude_OfAnEmptySubjectIsEmpty() =>
        Assert.True(SkiaPathOps.Exclude(QrPath.Empty, Square(0, 0, 1)).IsEmpty);
}
```

`tests/TrispotQR.Tests/ShapeFactoryTests.cs`:

```csharp
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Rendering;

namespace TrispotQR.Tests;

public class ShapeFactoryTests
{
    [Fact]
    public void SharpRectangle_IsFourLinesAndNoArcs()
    {
        var figure = ShapeFactory.RoundedRect(0, 0, 10, 0);

        Assert.Equal(new QrPoint(0, 0), figure.Start);
        Assert.True(figure.IsClosed);
        Assert.Equal(4, figure.Segments.Count);
        Assert.All(figure.Segments, s => Assert.IsType<QrLineTo>(s));
    }

    [Fact]
    public void RoundedRectangle_AlternatesLinesAndArcs()
    {
        var figure = ShapeFactory.RoundedRect(0, 0, 10, 2);

        Assert.Equal(8, figure.Segments.Count);
        Assert.Equal(4, figure.Segments.OfType<QrArcTo>().Count());
        Assert.All(figure.Segments.OfType<QrArcTo>(), a =>
        {
            Assert.Equal(2, a.Radius);
            Assert.True(a.Clockwise);
        });
    }

    /// <summary>
    /// A radius larger than half the shorter side would make opposite corners overlap and
    /// the arcs fold back on themselves.
    /// </summary>
    [Fact]
    public void Radius_IsClampedToHalfTheShorterSide()
    {
        var figure = ShapeFactory.RoundedRect(0, 0, 10, 4, 99, 99, 99, 99);
        Assert.All(figure.Segments.OfType<QrArcTo>(), a => Assert.Equal(2, a.Radius));
    }

    [Fact]
    public void Circle_IsARectangleRoundedByHalfItsSize()
    {
        var figure = ShapeFactory.Circle(0, 0, 8);
        Assert.All(figure.Segments.OfType<QrArcTo>(), a => Assert.Equal(4, a.Radius));
    }

    [Fact]
    public void Diamond_TouchesTheMiddleOfEachEdge()
    {
        var figure = ShapeFactory.Diamond(0, 0, 10);

        Assert.Equal(new QrPoint(5, 0), figure.Start);
        Assert.Equal(
            [new QrPoint(10, 5), new QrPoint(5, 10), new QrPoint(0, 5)],
            figure.Segments.Cast<QrLineTo>().Select(s => s.To));
    }

    [Fact]
    public void Offsets_ArePlacedWhereAsked() =>
        Assert.Equal(new QrPoint(3, 7), ShapeFactory.RoundedRect(3, 7, 2, 0).Start);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~ShapeFactoryTests|FullyQualifiedName~SkiaPathOpsTests"`
Expected: FAIL to compile, `SkiaPathOps` does not exist and `PathFigure` will not convert to `QrFigure`.

- [ ] **Step 3a: ShapeFactory returns QrFigure**

Replace the whole file. The maths is unchanged; only the types are.

```csharp
using TrispotQR.Core.Primitives;

namespace TrispotQR.Core.Rendering;

/// <summary>
/// Builds the primitive figures the code is drawn from. Everything is expressed in module
/// units and comes back as a <see cref="QrFigure"/>, so a layer can be assembled into one
/// path that the rasteriser and the SVG writer both understand without special cases.
/// </summary>
internal static class ShapeFactory
{
    /// <summary>
    /// A rectangle with an independent corner radius on each corner. Corners with a zero
    /// radius stay sharp. This one primitive covers squares, rounded squares, circles
    /// (all four radii at half the width), leaves and every fluid module.
    /// </summary>
    public static QrFigure RoundedRect(
        double x, double y, double width, double height,
        double topLeft, double topRight, double bottomRight, double bottomLeft)
    {
        // A radius can never exceed half the shorter side, or opposite corners overlap
        // and the arcs fold back on themselves.
        var limit = Math.Min(width, height) / 2.0;
        topLeft = Math.Clamp(topLeft, 0, limit);
        topRight = Math.Clamp(topRight, 0, limit);
        bottomRight = Math.Clamp(bottomRight, 0, limit);
        bottomLeft = Math.Clamp(bottomLeft, 0, limit);

        var right = x + width;
        var bottom = y + height;
        var segments = new List<QrSegment>(8);

        segments.Add(new QrLineTo(new QrPoint(right - topRight, y)));
        AddCorner(segments, topRight, right, y + topRight);

        segments.Add(new QrLineTo(new QrPoint(right, bottom - bottomRight)));
        AddCorner(segments, bottomRight, right - bottomRight, bottom);

        segments.Add(new QrLineTo(new QrPoint(x + bottomLeft, bottom)));
        AddCorner(segments, bottomLeft, x, bottom - bottomLeft);

        segments.Add(new QrLineTo(new QrPoint(x, y + topLeft)));
        AddCorner(segments, topLeft, x + topLeft, y);

        return new QrFigure(new QrPoint(x + topLeft, y), segments, IsClosed: true);
    }

    /// <summary>A square with every corner rounded by the same amount.</summary>
    public static QrFigure RoundedRect(double x, double y, double size, double radius) =>
        RoundedRect(x, y, size, size, radius, radius, radius, radius);

    /// <summary>A circle inscribed in the given square, drawn as a fully rounded rectangle.</summary>
    public static QrFigure Circle(double x, double y, double size) =>
        RoundedRect(x, y, size, size, size / 2, size / 2, size / 2, size / 2);

    /// <summary>A square rotated 45 degrees, with its points touching the middle of each edge.</summary>
    public static QrFigure Diamond(double x, double y, double size)
    {
        var half = size / 2;

        return new QrFigure(
            new QrPoint(x + half, y),
            [
                new QrLineTo(new QrPoint(x + size, y + half)),
                new QrLineTo(new QrPoint(x + half, y + size)),
                new QrLineTo(new QrPoint(x, y + half)),
            ],
            IsClosed: true);
    }

    private static void AddCorner(List<QrSegment> segments, double radius, double endX, double endY)
    {
        if (radius <= 0)
        {
            // No arc needed. The following line segment already starts at this corner.
            return;
        }

        segments.Add(new QrArcTo(new QrPoint(endX, endY), radius, Clockwise: true));
    }
}
```

- [ ] **Step 3b: SkiaPathOps**

```csharp
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
        return result is null ? subject : SkiaPath.ToQrPath(result, subject.FillRule);
    }
}
```

- [ ] **Step 3c: ImageSize**

`LogoCompositor.Place` currently reads the logo's pixel dimensions through
`QrRenderer.LoadImage`, which is WPF and is leaving Core. This replaces it.

```csharp
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
```

- [ ] **Step 3d: QrDrawing**

```csharp
using TrispotQR.Core.Primitives;

namespace TrispotQR.Core.Rendering;

/// <summary>Stable identifiers for the drawing layers, used by exporters and tests.</summary>
public static class QrLayerNames
{
    public const string Modules = "modules";
    public const string MarkerFrames = "marker-frames";
    public const string MarkerCenters = "marker-centers";
}

/// <summary>An outline drawn around a layer. Thickness is in module units.</summary>
public sealed record QrStroke(RgbColor Color, double Thickness);

/// <summary>
/// One painted layer of the code: a path plus how to fill and stroke it. Layers exist so
/// the data modules and the two parts of the corner markers can carry independent shapes
/// and colours while still being described by a single flat drawing.
/// </summary>
public sealed class QrLayer
{
    public QrLayer(string name, QrPath path, RgbColor fill, QrStroke? stroke)
    {
        Name = name;
        Path = path;
        Fill = fill;
        Stroke = stroke;
    }

    public string Name { get; }

    /// <summary>The layer's geometry, in module units.</summary>
    public QrPath Path { get; }

    public RgbColor Fill { get; }

    public QrStroke? Stroke { get; }
}

/// <summary>Where and how large the centre logo sits, in module units.</summary>
public sealed record LogoPlacement(string Path, double X, double Y, double Width, double Height)
{
    /// <summary>Share of the code area the logo covers, used for the scannability warning.</summary>
    public double CoverageRatio { get; init; }
}

/// <summary>
/// A complete, resolution independent description of a rendered code. Coordinates are in
/// module units with the quiet zone included, so the same drawing scales to a 96 px
/// preview thumbnail and a 4096 px print export with no second code path.
/// </summary>
public sealed class QrDrawing
{
    public QrDrawing(double sizeInUnits, RgbColor? background, IReadOnlyList<QrLayer> layers, LogoPlacement? logo)
    {
        SizeInUnits = sizeInUnits;
        Background = background;
        Layers = layers;
        Logo = logo;
    }

    /// <summary>Width and height of the whole canvas in module units, quiet zone included.</summary>
    public double SizeInUnits { get; }

    /// <summary>Background colour, or null for transparent.</summary>
    public RgbColor? Background { get; }

    /// <summary>Painted back to front: modules, then marker frames, then marker centres.</summary>
    public IReadOnlyList<QrLayer> Layers { get; }

    public LogoPlacement? Logo { get; }
}
```

- [ ] **Step 3e: QrGeometryBuilder**

Change `using System.Windows.Media;` to `using TrispotQR.Core.Primitives;`. Each of the three
layer builders swaps its `PathGeometry` for a `QrPathBuilder`; the loops and the shape
selection are untouched. For the module layer:

```csharp
    private static QrLayer BuildModuleLayer(QrMatrix matrix, QrStyle style, int quiet)
    {
        var builder = new QrPathBuilder();

        for (var y = 0; y < matrix.Size; y++)
        {
            for (var x = 0; x < matrix.Size; x++)
            {
                if (!matrix.IsDark(x, y) || matrix.IsFinderPattern(x, y))
                {
                    continue;
                }

                builder.Add(ModuleFigure(matrix, style, x, y, quiet));
            }
        }

        return new QrLayer(
            QrLayerNames.Modules,
            builder.Build(QrFillRule.NonZero),
            style.Foreground,
            StrokeFor(style, OutlineTarget.Modules));
    }
```

Apply the same change to `BuildMarkerFrameLayer` (fill `style.EffectiveMarkerFrameColor`,
stroke target `OutlineTarget.Markers`) and `BuildMarkerCenterLayer` (fill
`style.EffectiveMarkerCenterColor`, same stroke target). `ModuleFigure`, `FrameFigure`,
`OuterCorner` and the `Corner` enum change only their return type from `PathFigure` to
`QrFigure`.

Replace `PenFor` with `StrokeFor`, **preserving the existing predicate exactly**:

```csharp
    /// <summary>The outline for a layer, or null when this style does not outline it.</summary>
    private static QrStroke? StrokeFor(QrStyle style, OutlineTarget part)
    {
        var outline = style.Outline;

        if (!outline.Enabled)
        {
            return null;
        }

        var applies = outline.Target == OutlineTarget.Both || outline.Target == part;
        if (!applies)
        {
            return null;
        }

        return new QrStroke(outline.Color, outline.ThicknessRatio);
    }
```

Replace the punch with the Skia op, and delete the `Brush`, `Normalise` and `PenFor`
helpers along with every `Freeze` call: `QrPath` is immutable by construction.

```csharp
    private static QrLayer Punched(QrLayer layer, QrPath punch) =>
        new(layer.Name, SkiaPathOps.Exclude(layer.Path, punch), layer.Fill, layer.Stroke);
```

- [ ] **Step 3f: LogoCompositor**

Two changes only. `Place` reads dimensions through `ImageSize` instead of
`QrRenderer.LoadImage`:

```csharp
        var size = ImageSize.Read(style.Logo.Path);
        if (size is not { } dimensions)
        {
            return null;
        }

        var box = style.Logo.SizeRatio * matrix.Size;
        var aspect = (double)dimensions.Width / dimensions.Height;
```

`Punch` returns `QrPath?`. **Keep the existing body**, including `PunchRect` and
`RoundedRadius`, and change only the geometry construction:

```csharp
    /// <summary>
    /// The area cleared behind the logo: its box plus the configured padding on every
    /// side. Null when the style asks for no punch at all.
    /// </summary>
    public static QrPath? Punch(LogoPlacement placement, QrStyle style)
    {
        if (style.Logo.PunchShape == LogoPunchShape.None)
        {
            return null;
        }

        var rect = PunchRect(placement, style);

        var figure = style.Logo.PunchShape switch
        {
            LogoPunchShape.Circle => ShapeFactory.RoundedRect(
                rect.X, rect.Y, rect.Width, rect.Height,
                rect.Width / 2, rect.Width / 2, rect.Width / 2, rect.Width / 2),

            LogoPunchShape.RoundedSquare => ShapeFactory.RoundedRect(
                rect.X, rect.Y, rect.Width, rect.Height,
                RoundedRadius(rect), RoundedRadius(rect), RoundedRadius(rect), RoundedRadius(rect)),

            _ => ShapeFactory.RoundedRect(rect.X, rect.Y, rect.Width, rect.Height, 0, 0, 0, 0),
        };

        return new QrPathBuilder().Add(figure).Build(QrFillRule.NonZero);
    }
```

`PunchRect` currently returns a WPF `Rect`. Replace it with a local record so Core sheds the
dependency, keeping the arithmetic identical:

```csharp
    private sealed record PunchArea(double X, double Y, double Width, double Height);

    private static PunchArea PunchRect(LogoPlacement placement, QrStyle style)
    {
        var padding = style.Logo.PunchPadding;
        return new PunchArea(
            placement.X - padding,
            placement.Y - padding,
            placement.Width + (padding * 2),
            placement.Height + (padding * 2));
    }

    private static double RoundedRadius(PunchArea rect) => Math.Min(rect.Width, rect.Height) * 0.18;
```

- [ ] **Step 3g: Colour swap across Styling and Presets**

In `QrStyle.cs`, `StylePresets.cs` and `HsvColor.cs`: delete `using System.Windows.Media;`,
add `using TrispotQR.Core.Primitives;`, and replace every `Color` with `RgbColor`,
`Colors.Black` with `RgbColor.Black`, `Colors.White` with `RgbColor.White`, and
`Color.FromRgb(...)` with `RgbColor.FromRgb(...)`. Property names do not change.

`ColorJsonConverter` becomes:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using TrispotQR.Core.Primitives;

namespace TrispotQR.Core.Presets;

/// <summary>
/// Reads and writes <see cref="RgbColor"/> as a hex string, so a preset file stays
/// something a person can open and edit rather than a wall of channel numbers. Alpha is
/// written only when it is not fully opaque.
///
/// The format is unchanged from the version that stored a WPF colour, so preset files
/// written before the port still load.
/// </summary>
public sealed class ColorJsonConverter : JsonConverter<RgbColor>
{
    public override RgbColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();

        return RgbColor.TryParse(text, out var colour)
            ? colour
            : throw new JsonException($"\"{text}\" is not a colour this app understands.");
    }

    public override void Write(Utf8JsonWriter writer, RgbColor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToHex());
}
```

- [ ] **Step 3h: The throwaway WPF adapter**

`src/TrispotQR.App/Rendering/WpfGeometryAdapter.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Rendering;

namespace TrispotQR.App.Rendering;

/// <summary>
/// Converts the app's own geometry and colour types into WPF's.
///
/// THROWAWAY. This exists only so the existing WPF window keeps working while Core is
/// freed from WPF. Phase 2 replaces the window with Avalonia and deletes this file.
/// </summary>
internal static class WpfGeometryAdapter
{
    public static Color ToColor(RgbColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    public static Color? ToColor(RgbColor? c) => c is { } value ? ToColor(value) : null;

    public static Brush ToBrush(RgbColor c)
    {
        var brush = new SolidColorBrush(ToColor(c));
        brush.Freeze();
        return brush;
    }

    public static Pen? ToPen(QrStroke? stroke)
    {
        if (stroke is null)
        {
            return null;
        }

        var pen = new Pen(ToBrush(stroke.Color), stroke.Thickness) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }

    public static Geometry ToGeometry(QrPath path)
    {
        var geometry = new PathGeometry
        {
            FillRule = path.FillRule == QrFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.Nonzero,
        };

        foreach (var figure in path.Figures)
        {
            var wpf = new PathFigure
            {
                StartPoint = new Point(figure.Start.X, figure.Start.Y),
                IsClosed = figure.IsClosed,
                IsFilled = true,
            };

            foreach (var segment in figure.Segments)
            {
                wpf.Segments.Add(segment switch
                {
                    QrLineTo line => new LineSegment(new Point(line.To.X, line.To.Y), true),

                    QrArcTo arc => new ArcSegment
                    {
                        Point = new Point(arc.To.X, arc.To.Y),
                        Size = new Size(arc.Radius, arc.Radius),
                        SweepDirection = arc.Clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                        IsLargeArc = false,
                        RotationAngle = 0,
                    },

                    QrCubicTo cubic => new BezierSegment(
                        new Point(cubic.C1.X, cubic.C1.Y),
                        new Point(cubic.C2.X, cubic.C2.Y),
                        new Point(cubic.To.X, cubic.To.Y),
                        true),

                    _ => throw new NotSupportedException($"Unknown segment {segment.GetType().Name}"),
                });
            }

            wpf.Freeze();
            geometry.Figures.Add(wpf);
        }

        geometry.Freeze();
        return geometry;
    }
}
```

- [ ] **Step 3i: Move QrRenderer to the app**

`git mv src/TrispotQR.Core/Rendering/QrRenderer.cs src/TrispotQR.App/Rendering/WpfQrRenderer.cs`.
Rename the class to `WpfQrRenderer`, change the namespace to `TrispotQR.App.Rendering`, add
`using TrispotQR.Core.Primitives;` and `using TrispotQR.Core.Rendering;`, and change all
three `Color?` parameters to `RgbColor?`. Inside, convert at the point of use:

```csharp
            var background = backgroundOverride ?? drawing.Background;
            if (background is { } colour && colour.A > 0)
            {
                var brush = WpfGeometryAdapter.ToBrush(colour);
                context.DrawRectangle(brush, null, new Rect(0, 0, drawing.SizeInUnits, drawing.SizeInUnits));
            }

            foreach (var layer in drawing.Layers)
            {
                context.DrawGeometry(
                    WpfGeometryAdapter.ToBrush(layer.Fill),
                    WpfGeometryAdapter.ToPen(layer.Stroke),
                    WpfGeometryAdapter.ToGeometry(layer.Path));
            }
```

`LoadImage` stays on `WpfQrRenderer` for the app's own preview and logo drawing. Update
every reference across the app and tests from `QrRenderer` to `WpfQrRenderer`.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test TrispotQR.slnx`
Expected: PASS, every existing test plus the new ones. Fix compile errors in the app and
tests by swapping `Color` for `RgbColor` and `QrRenderer` for `WpfQrRenderer`. Do not change
the meaning of any existing assertion. If a style-matrix test starts failing, the fault is
the arc conversion in `SkiaPath`, not the geometry.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Move Core's geometry and colour onto its own types

QrPath replaces WPF Geometry and RgbColor replaces WPF Color throughout
Core. The WPF app keeps rendering through a throwaway adapter that Phase 2
deletes with the rest of the window.

The logo punch-out goes through a Skia boolean op rather than
Geometry.Combine, and the logo's aspect ratio is read from the file header
by SkiaSharp rather than by decoding it through WPF."
```

---

## Task 6: The Skia rasteriser

**Files:**
- Create: `src/TrispotQR.Core/Rendering/SkiaRasterizer.cs`
- Test: `tests/TrispotQR.Tests/SkiaRasterizerTests.cs`

**Interfaces:**
- Consumes: `QrDrawing`, `QrLayer`, `RgbColor`, `SkiaPath`.
- Produces: `public sealed record RasterImage(int Width, int Height, byte[] Pixels)`, pixels being BGRA, four bytes each, premultiplied; `public static class SkiaRasterizer` with `RasterImage Render(QrDrawing, int pixelSize, RgbColor? backgroundOverride = null)` and `byte[] EncodePng(RasterImage)`.

- [ ] **Step 1: Write the failing test**

```csharp
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;

namespace TrispotQR.Tests;

public class SkiaRasterizerTests
{
    private static QrDrawing Build(QrStyle? style = null)
    {
        var encoded = QrEncoder.Encode("https://example.org", EccLevel.Medium);
        return QrGeometryBuilder.Build(encoded.Matrix!, style ?? QrStyle.Default);
    }

    private static byte AlphaAt(RasterImage image, int x, int y) =>
        image.Pixels[(((y * image.Width) + x) * 4) + 3];

    [Fact]
    public void Render_ProducesTheRequestedSize()
    {
        var image = SkiaRasterizer.Render(Build(), 256);

        Assert.Equal(256, image.Width);
        Assert.Equal(256, image.Height);
        Assert.Equal(256 * 256 * 4, image.Pixels.Length);
    }

    [Fact]
    public void AWhiteBackground_IsOpaque() =>
        Assert.Equal(255, AlphaAt(SkiaRasterizer.Render(Build(QrStyle.Default with { Background = RgbColor.White }), 128), 2, 2));

    /// <summary>
    /// The quiet zone of a transparent code must have a genuinely empty alpha channel, not
    /// white pixels. The whole transparent-PNG feature rests on this.
    /// </summary>
    [Fact]
    public void NoBackground_LeavesTheQuietZoneTransparent() =>
        Assert.Equal(0, AlphaAt(SkiaRasterizer.Render(Build(QrStyle.Default with { Background = null }), 128), 2, 2));

    [Fact]
    public void BackgroundOverride_FlattensATransparentCode() =>
        Assert.Equal(255, AlphaAt(
            SkiaRasterizer.Render(Build(QrStyle.Default with { Background = null }), 128, RgbColor.White), 2, 2));

    [Fact]
    public void EncodePng_ProducesARealPngHeader() =>
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], SkiaRasterizer.EncodePng(SkiaRasterizer.Render(Build(), 64)).Take(4));

    [Fact]
    public void Render_RejectsANonPositiveSize() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SkiaRasterizer.Render(Build(), 0));

    /// <summary>The code itself must actually be painted, not just the background.</summary>
    [Fact]
    public void Render_PaintsDarkModules()
    {
        var image = SkiaRasterizer.Render(Build(QrStyle.Default with { Background = RgbColor.White }), 256);
        var dark = 0;

        for (var i = 0; i < image.Pixels.Length; i += 4)
        {
            if (image.Pixels[i] < 64)
            {
                dark++;
            }
        }

        Assert.True(dark > 1000, $"only {dark} dark pixels, the code was not drawn");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~SkiaRasterizerTests"`
Expected: FAIL to compile, "The name 'SkiaRasterizer' does not exist".

- [ ] **Step 3: Write minimal implementation**

Note the explicit `using System.IO;`: Core is still a `UseWPF` project, where it is not implicit.

```csharp
using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;
using TrispotQR.Core.Primitives;

namespace TrispotQR.Core.Rendering;

/// <summary>Raw pixels: BGRA, four bytes each, premultiplied, top row first.</summary>
public sealed record RasterImage(int Width, int Height, byte[] Pixels);

/// <summary>
/// Paints a <see cref="QrDrawing"/> into pixels.
///
/// Skia rather than any UI toolkit, because this has to work with no window, no display
/// and no UI thread: PNG export, the preset thumbnails and the scannability check all run
/// off-screen, and two of them run on a background thread. It also renders identically on
/// Windows, macOS and Linux, which a per-platform toolkit would not.
/// </summary>
public static class SkiaRasterizer
{
    /// <param name="backgroundOverride">
    /// Forces a background colour regardless of the drawing's own. Used to flatten a
    /// transparent code onto white before decoding, which is what a scanner would see.
    /// </param>
    public static RasterImage Render(QrDrawing drawing, int pixelSize, RgbColor? backgroundOverride = null)
    {
        ArgumentNullException.ThrowIfNull(drawing);

        if (pixelSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelSize), "Pixel size must be positive.");
        }

        // Premultiplied so a code with no background stays genuinely transparent rather
        // than being flattened onto white here.
        var info = new SKImageInfo(pixelSize, pixelSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale((float)(pixelSize / drawing.SizeInUnits));

        var background = backgroundOverride ?? drawing.Background;
        if (background is { } colour && colour.A > 0)
        {
            using var fill = new SKPaint { Color = ToSk(colour), IsAntialias = false, Style = SKPaintStyle.Fill };
            canvas.DrawRect(0, 0, (float)drawing.SizeInUnits, (float)drawing.SizeInUnits, fill);
        }

        foreach (var layer in drawing.Layers)
        {
            DrawLayer(canvas, layer);
        }

        DrawLogo(canvas, drawing);

        canvas.Restore();
        canvas.Flush();

        var pixels = new byte[info.BytesSize];

        using (var image = surface.Snapshot())
        using (var bitmap = SKBitmap.FromImage(image))
        {
            Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        }

        return new RasterImage(pixelSize, pixelSize, pixels);
    }

    public static byte[] EncodePng(RasterImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);

        try
        {
            using var bitmap = new SKBitmap();
            bitmap.InstallPixels(info, handle.AddrOfPinnedObject(), info.RowBytes);

            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }

    private static void DrawLayer(SKCanvas canvas, QrLayer layer)
    {
        if (layer.Path.IsEmpty)
        {
            return;
        }

        using var path = SkiaPath.ToSKPath(layer.Path);

        using var fill = new SKPaint
        {
            Color = ToSk(layer.Fill),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        canvas.DrawPath(path, fill);

        if (layer.Stroke is { } stroke)
        {
            using var pen = new SKPaint
            {
                Color = ToSk(stroke.Color),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)stroke.Thickness,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
            };
            canvas.DrawPath(path, pen);
        }
    }

    private static void DrawLogo(SKCanvas canvas, QrDrawing drawing)
    {
        if (drawing.Logo is not { } logo || !File.Exists(logo.Path))
        {
            return;
        }

        using var stream = File.OpenRead(logo.Path);
        using var bitmap = SKBitmap.Decode(stream);

        if (bitmap is null)
        {
            return;
        }

        canvas.DrawBitmap(bitmap, new SKRect(
            (float)logo.X, (float)logo.Y, (float)(logo.X + logo.Width), (float)(logo.Y + logo.Height)));
    }

    private static SKColor ToSk(RgbColor c) => new(c.R, c.G, c.B, c.A);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~SkiaRasterizerTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/TrispotQR.Core/Rendering/SkiaRasterizer.cs tests/TrispotQR.Tests/SkiaRasterizerTests.cs
git commit -m "Rasterise with Skia, off-screen and with no UI framework"
```

---

## Task 7: PngExporter and QrDecoder on raw pixels

**Files:**
- Modify: `src/TrispotQR.Core/Export/PngExporter.cs`, `src/TrispotQR.Core/Validation/QrDecoder.cs`, `src/TrispotQR.Core/Validation/ScannabilityChecker.cs`
- Create: `tests/TrispotQR.Tests/PngExporterTests.cs`
- Modify: every test that renders then decodes

**Interfaces:**
- Consumes: `RasterImage`, `SkiaRasterizer`.
- Produces: `PngExporter.Save(RasterImage, string)`, `PngExporter.ToBytes(RasterImage)`, `QrDecoder.Decode(RasterImage)`, `QrDecoder.DecodeStrict(RasterImage)`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using TrispotQR.Core.Export;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.Core.Validation;

namespace TrispotQR.Tests;

public class PngExporterTests : IDisposable
{
    private const string Payload = "https://example.org/tickets";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"trispotqr-png-{Guid.NewGuid():N}");

    public PngExporterTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static RasterImage Render(RgbColor? background, int size = 256)
    {
        var encoded = QrEncoder.Encode(Payload, EccLevel.Medium);
        var drawing = QrGeometryBuilder.Build(encoded.Matrix!, QrStyle.Default with { Background = background });
        return SkiaRasterizer.Render(drawing, size);
    }

    [Fact]
    public void Save_WritesAFileThatDecodesBack()
    {
        var path = Path.Combine(_directory, "code.png");
        PngExporter.Save(Render(RgbColor.White), path);

        Assert.True(File.Exists(path));
        Assert.Equal(Payload, QrDecoder.Decode(Render(RgbColor.White)));
    }

    [Fact]
    public void Save_CreatesMissingDirectories()
    {
        var path = Path.Combine(_directory, "nested", "deeper", "code.png");
        PngExporter.Save(Render(RgbColor.White), path);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        var path = Path.Combine(_directory, "code.png");
        PngExporter.Save(Render(RgbColor.White), path);

        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void ToBytes_IsAPng() =>
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], PngExporter.ToBytes(Render(RgbColor.White)).Take(4));

    /// <summary>A code saved with no background must stay genuinely transparent.</summary>
    [Fact]
    public void ATransparentCode_KeepsItsAlphaChannel() => Assert.Equal(0, Render(background: null).Pixels[3]);

    [Fact]
    public void ATransparentCode_StillDecodesBecauseDecodingFlattensOntoWhite() =>
        Assert.Equal(Payload, QrDecoder.Decode(Render(background: null)));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~PngExporterTests"`
Expected: FAIL to compile, `RasterImage` cannot convert to `BitmapSource`.

- [ ] **Step 3a: PngExporter**

```csharp
using System.IO;
using TrispotQR.Core.Rendering;

namespace TrispotQR.Core.Export;

/// <summary>
/// Writes a rendered code out as a PNG. The encoder keeps whatever alpha the image
/// carries, so a code rendered with no background saves as a genuinely transparent file.
/// </summary>
public static class PngExporter
{
    public static void Save(RasterImage image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Written to a temporary file and moved into place so an interrupted save cannot
        // leave a half-written PNG where a good one used to be.
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, ToBytes(image));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The encoded PNG as bytes, for the clipboard and for tests.</summary>
    public static byte[] ToBytes(RasterImage image) => SkiaRasterizer.EncodePng(image);
}
```

- [ ] **Step 3b: QrDecoder**

Keep the two-pass doc comment exactly as it stands today; only the input type and the
flattening change.

```csharp
using TrispotQR.Core.Rendering;
using ZXing;

namespace TrispotQR.Core.Validation;

public static class QrDecoder
{
    public static string? Decode(RasterImage image) => DecodeStrict(image) ?? Read(image, pureBarcode: true);

    public static string? DecodeStrict(RasterImage image) => Read(image, pureBarcode: false);

    private static string? Read(RasterImage image, bool pureBarcode)
    {
        ArgumentNullException.ThrowIfNull(image);

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = false,
            Options = new ZXing.Common.DecodingOptions
            {
                PossibleFormats = new[] { BarcodeFormat.QR_CODE },
                TryHarder = true,
                PureBarcode = pureBarcode,
            },
        };

        var result = reader.Decode(
            FlattenToBgr24(image), image.Width, image.Height, RGBLuminanceSource.BitmapFormat.BGR24);

        return result?.Text;
    }

    /// <summary>
    /// Drops the alpha channel by compositing over white. A transparent PNG has no
    /// background of its own, and a scanner looking at it on paper sees white, so that is
    /// what the decoder is given.
    ///
    /// The source is premultiplied, so the colour channels are already scaled by alpha and
    /// compositing over white is simply adding back the uncovered remainder.
    /// </summary>
    private static byte[] FlattenToBgr24(RasterImage image)
    {
        var result = new byte[image.Width * image.Height * 3];

        for (int source = 0, target = 0; source < image.Pixels.Length; source += 4, target += 3)
        {
            var uncovered = 255 - image.Pixels[source + 3];

            result[target] = (byte)(image.Pixels[source] + uncovered);
            result[target + 1] = (byte)(image.Pixels[source + 1] + uncovered);
            result[target + 2] = (byte)(image.Pixels[source + 2] + uncovered);
        }

        return result;
    }
}
```

- [ ] **Step 3c: Update every call site**

`ScannabilityChecker`, `RenderAndDecodeTests`, `StyleMatrixScanTests`,
`ScannabilityFalseAlarmTests`, `LogoTests`, `SvgExporterTests`, `MainViewModel` and
`ExportGuardTests` all render then decode. Replace `WpfQrRenderer.RenderToBitmap(drawing, size)`
with `SkiaRasterizer.Render(drawing, size)`. In `ScannabilityChecker`, delete the
`StaThread.Run` wrapper and its `using`: Skia needs no apartment thread. The app's
`MainViewModel` keeps using `WpfQrRenderer` for the on-screen preview only.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test TrispotQR.slnx`
Expected: PASS. Watch the style matrix; a shape that stops decoding points at `SkiaPath` arc
conversion.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Export and decode from raw pixels rather than a WPF bitmap"
```

---

## Task 8: Prove the Skia renderer is faithful

The most valuable test in this phase. It runs only while both renderers exist, so it must be
written now and is deleted in Phase 2 with the WPF app.

**Files:**
- Create: `tests/TrispotQR.Tests/RendererEquivalenceTests.cs`

**Interfaces:**
- Consumes: `WpfQrRenderer`, `SkiaRasterizer`, `QrDecoder`, `StylePresets`.
- Produces: nothing. A test only.

- [ ] **Step 1: Write the test**

```csharp
using System.Windows.Media.Imaging;
using TrispotQR.App.Rendering;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.Core.Validation;

namespace TrispotQR.Tests;

/// <summary>
/// The Skia renderer must produce codes that read back the same as the WPF renderer's.
///
/// This is the guard for the whole port. Pixel-identical output is explicitly not the goal,
/// because two engines antialias differently and it would fail for reasons nobody should
/// care about. What matters is that a code rendered the new way still scans and still
/// carries the same content, for every style the app can produce.
///
/// Deleted in Phase 2 along with the WPF renderer it compares against.
/// </summary>
[Collection("UI")]
public class RendererEquivalenceTests
{
    private const string Payload = "https://example.org/equivalence";

    private readonly WpfHost _host;

    public RendererEquivalenceTests(WpfHost host) => _host = host;

    public static TheoryData<string> Presets
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var preset in StylePresets.BuiltIn)
            {
                data.Add(preset.Name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void BothRenderers_ReadBackTheSameContent(string presetName)
    {
        var style = StylePresets.BuiltIn.Single(p => p.Name == presetName).Style;
        var encoded = QrEncoder.Encode(Payload, style.Ecc);
        var drawing = QrGeometryBuilder.Build(encoded.Matrix!, style);

        var skia = QrDecoder.Decode(SkiaRasterizer.Render(drawing, 512, RgbColor.White));
        var wpf = _host.Run(() => DecodeWpf(WpfQrRenderer.RenderToBitmap(drawing, 512, RgbColor.White)));

        Assert.Equal(Payload, wpf);
        Assert.Equal(Payload, skia);
    }

    /// <summary>
    /// Every shape combination, not just the presets. The same guard StyleMatrixScanTests
    /// applies, pointed at whether the renderer swap changed anything.
    /// </summary>
    [Fact]
    public void EveryShapeCombination_StillDecodesUnderSkia()
    {
        var failures = new List<string>();

        foreach (var module in Enum.GetValues<ModuleShape>())
        {
            foreach (var frame in Enum.GetValues<MarkerFrameShape>())
            {
                foreach (var centre in Enum.GetValues<MarkerCenterShape>())
                {
                    var style = QrStyle.Default with
                    {
                        ModuleShape = module,
                        MarkerFrameShape = frame,
                        MarkerCenterShape = centre,
                        Background = RgbColor.White,
                    };

                    var encoded = QrEncoder.Encode(Payload, style.Ecc);
                    var drawing = QrGeometryBuilder.Build(encoded.Matrix!, style);
                    var read = QrDecoder.Decode(SkiaRasterizer.Render(drawing, 512));

                    if (read != Payload)
                    {
                        failures.Add($"{module}/{frame}/{centre} read back as {read ?? "nothing"}");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>Pbgra32 is premultiplied BGRA, which is exactly what RasterImage carries.</summary>
    private static string? DecodeWpf(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        return QrDecoder.Decode(new RasterImage(bitmap.PixelWidth, bitmap.PixelHeight, pixels));
    }
}
```

- [ ] **Step 2: Run it**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~RendererEquivalenceTests"`
Expected: PASS. A combination failing only under Skia points at `SkiaPath.ToSKPath`: check
`SKPathArcSize.Small` and the sweep direction against what `ShapeFactory` intended.

- [ ] **Step 3: Commit**

```bash
git add tests/TrispotQR.Tests/RendererEquivalenceTests.cs
git commit -m "Prove the Skia renderer reads back the same as the WPF one"
```

---

## Task 9: SvgExporter on the model

**Files:**
- Modify: `src/TrispotQR.Core/Export/SvgExporter.cs`, `tests/TrispotQR.Tests/SvgExporterTests.cs`

**Interfaces:**
- Consumes: `SvgPathData`, `QrDrawing`, `RgbColor`.
- Produces: unchanged public surface, `Save(QrDrawing, int, string)` and `ToSvg(QrDrawing, int)`.

- [ ] **Step 1: Write the failing test**

Add to `SvgExporterTests`:

```csharp
    [Fact]
    public void ThePathData_HasNoWpfFillRuleTokenLeftInIt()
    {
        var svg = SvgExporter.ToSvg(Drawing(), 512);

        Assert.DoesNotContain("d=\"F0", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("d=\"F1", svg, StringComparison.Ordinal);
        Assert.Contains("fill-rule=\"nonzero\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPath_StartsWithAMoveCommand()
    {
        var svg = SvgExporter.ToSvg(Drawing(), 512);

        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(svg, @"d=""([^""]*)"""))
        {
            Assert.StartsWith("M", match.Groups[1].Value, StringComparison.Ordinal);
        }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~SvgExporterTests"`
Expected: FAIL to compile, `PathData(Geometry)` no longer applies.

- [ ] **Step 3: Write minimal implementation**

Delete `using System.Windows.Media;`, add `using TrispotQR.Core.Primitives;`, and delete the
whole `PathData` method with its comment about WPF's mini-language. Then:

```csharp
    private static void AppendLayer(StringBuilder builder, QrLayer layer)
    {
        var data = SvgPathData.ToData(layer.Path);

        if (data.Length == 0)
        {
            return;
        }

        builder.Append(Invariant, $@"  <path id=""{layer.Name}"" d=""{data}""");
        builder.Append(Invariant, $@" fill=""{Hex(layer.Fill)}""{Opacity(layer.Fill)}");
        builder.Append(Invariant, $@" fill-rule=""{SvgPathData.FillRule(layer.Path.FillRule)}""");

        if (layer.Stroke is { } stroke)
        {
            builder.Append(Invariant, $@" stroke=""{Hex(stroke.Color)}""");
            builder.Append(Invariant, $@" stroke-width=""{stroke.Thickness.ToString("0.####", Invariant)}""");
            builder.Append(@" stroke-linejoin=""round""");
        }

        builder.AppendLine(" />");
    }

    private static string Hex(RgbColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>SVG carries alpha separately, so a partly transparent colour needs the extra attribute.</summary>
    private static string Opacity(RgbColor color) =>
        color.A == 255 ? string.Empty : $@" fill-opacity=""{(color.A / 255.0).ToString("0.###", Invariant)}""";
```

The background rect already tests `drawing.Background is { A: > 0 }`, which works unchanged
on `RgbColor?`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~SvgExporter"`
Expected: PASS, the existing tests plus the two new ones.

- [ ] **Step 5: Commit**

```bash
git add src/TrispotQR.Core/Export/SvgExporter.cs tests/TrispotQR.Tests/SvgExporterTests.cs
git commit -m "Write SVG from the model instead of scraping WPF geometry"
```

---

## Task 10: Clipboard behind an interface

**Files:**
- Create: `src/TrispotQR.Core/Export/IImageClipboard.cs`, `src/TrispotQR.App/Export/WpfImageClipboard.cs`
- Delete: `src/TrispotQR.Core/Export/ClipboardExporter.cs`
- Modify: `src/TrispotQR.App/ViewModels/MainViewModel.cs`, `tests/TrispotQR.Tests/ClipboardExporterTests.cs`

**Interfaces:**
- Consumes: `RasterImage`, `PngExporter`.
- Produces: `public interface IImageClipboard { void Copy(RasterImage image); }`; `WpfImageClipboard : IImageClipboard`.

- [ ] **Step 1: Write the failing test**

Point the existing tests at `WpfImageClipboard`, keeping every assertion, and add:

```csharp
    /// <summary>Core must not know how a clipboard works on any particular platform.</summary>
    [Fact]
    public void TheContract_LivesInCoreAndTheImplementationDoesNot()
    {
        var contract = typeof(TrispotQR.Core.Export.IImageClipboard);

        Assert.True(contract.IsInterface);
        Assert.Single(contract.GetMethods());
        Assert.Contains("TrispotQR.App", typeof(TrispotQR.App.Export.WpfImageClipboard).Assembly.FullName);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~Clipboard"`
Expected: FAIL to compile, `IImageClipboard` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using TrispotQR.Core.Rendering;

namespace TrispotQR.Core.Export;

/// <summary>
/// Puts an image on the system clipboard.
///
/// An interface because every platform advertises image formats differently, and because
/// Core must stay free of anything assuming a desktop. The Windows implementation lives in
/// the app; Phase 2 adds an Avalonia one beside it.
/// </summary>
public interface IImageClipboard
{
    /// <summary>Copies the image, throwing if the clipboard could not be written.</summary>
    void Copy(RasterImage image);
}
```

Move the body of `ClipboardExporter` into `src/TrispotQR.App/Export/WpfImageClipboard.cs` as
`public sealed class WpfImageClipboard : IImageClipboard`. **Keep the comment explaining why
the `MemoryStream` is not disposed** — that is a bug this project already paid for once.
Take `RasterImage` and go through `PngExporter.ToBytes`:

```csharp
    public void Copy(RasterImage image)
    {
        var data = new DataObject();

        // The stream is deliberately not disposed. The DataObject only holds a reference to
        // it, and Clipboard.SetDataObject reads it during the flush at the end of this
        // method. Closing it first leaves a "PNG" format advertised on the clipboard whose
        // data is null, which pastes as nothing in every app that asks for PNG first.
        var png = new MemoryStream(PngExporter.ToBytes(image));
        data.SetData("PNG", png, autoConvert: false);
        data.SetImage(FlattenOntoWhite(png.ToArray()));

        SetWithRetry(data);
        VerifyLanded();
    }
```

`FlattenOntoWhite` decodes those PNG bytes into a `BitmapImage` and composites onto white
exactly as the old code did. `MainViewModel` takes `IImageClipboard` in its constructor,
defaulting to `new WpfImageClipboard()`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~Clipboard"`
Expected: PASS. These touch the real clipboard and are already in the non-parallel `UI`
collection; leave them there.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Put the clipboard behind an interface and move Windows code to the app"
```

---

## Task 11: Settings location per platform

**Files:**
- Create: `src/TrispotQR.Core/Presets/ISettingsLocation.cs`, `src/TrispotQR.Core/Presets/DesktopSettingsLocation.cs`
- Modify: `src/TrispotQR.Core/Presets/PresetStore.cs`
- Test: `tests/TrispotQR.Tests/SettingsLocationTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public interface ISettingsLocation { string Directory { get; } }`; `DesktopSettingsLocation` with instance `Directory` and `static string ResolveFor(OSPlatform, string home, string? appData, string? xdgConfigHome)`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using System.Runtime.InteropServices;
using TrispotQR.Core.Presets;

namespace TrispotQR.Tests;

public class SettingsLocationTests
{
    [Fact]
    public void Windows_UsesAppData() =>
        Assert.Equal(
            Path.Combine(@"C:\Users\x\AppData\Roaming", "TrispotQR"),
            DesktopSettingsLocation.ResolveFor(OSPlatform.Windows, @"C:\Users\x", @"C:\Users\x\AppData\Roaming", null));

    [Fact]
    public void MacOs_UsesApplicationSupport() =>
        Assert.Equal(
            "/Users/x/Library/Application Support/TrispotQR",
            DesktopSettingsLocation.ResolveFor(OSPlatform.OSX, "/Users/x", null, null).Replace('\\', '/'));

    [Fact]
    public void Linux_PrefersXdgConfigHome() =>
        Assert.Equal(
            "/home/x/.config/TrispotQR",
            DesktopSettingsLocation.ResolveFor(OSPlatform.Linux, "/home/x", null, "/home/x/.config").Replace('\\', '/'));

    [Fact]
    public void Linux_FallsBackToDotConfig() =>
        Assert.Equal(
            "/home/x/.config/TrispotQR",
            DesktopSettingsLocation.ResolveFor(OSPlatform.Linux, "/home/x", null, null).Replace('\\', '/'));

    [Fact]
    public void TheLiveLocation_IsAnAbsolutePath() =>
        Assert.True(Path.IsPathRooted(new DesktopSettingsLocation().Directory));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~SettingsLocationTests"`
Expected: FAIL to compile, `DesktopSettingsLocation` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.IO;
using System.Runtime.InteropServices;

namespace TrispotQR.Core.Presets;

/// <summary>
/// Where saved styles and settings live.
///
/// An interface because the answer differs on every platform, and differs again on mobile,
/// where the app writes into its own sandbox. Core asks; it does not decide.
/// </summary>
public interface ISettingsLocation
{
    string Directory { get; }
}

/// <summary>The desktop answer: the conventional per-user configuration folder for the OS.</summary>
public sealed class DesktopSettingsLocation : ISettingsLocation
{
    public const string FolderName = "TrispotQR";

    public string Directory => ResolveFor(
        Current(),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));

    /// <summary>
    /// Split out from <see cref="Directory"/> so every platform's answer can be tested from
    /// any platform, which is the only way this gets checked before Phase 3.
    /// </summary>
    public static string ResolveFor(OSPlatform platform, string home, string? appData, string? xdgConfigHome)
    {
        if (platform == OSPlatform.Windows)
        {
            return Path.Combine(appData ?? Path.Combine(home, "AppData", "Roaming"), FolderName);
        }

        if (platform == OSPlatform.OSX)
        {
            return Path.Combine(home, "Library", "Application Support", FolderName);
        }

        var config = string.IsNullOrWhiteSpace(xdgConfigHome) ? Path.Combine(home, ".config") : xdgConfigHome;
        return Path.Combine(config, FolderName);
    }

    private static OSPlatform Current()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return OSPlatform.Windows;
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX : OSPlatform.Linux;
    }
}
```

In `PresetStore`, the `Resolved` lazy asks the location, then carries over as it already does:

```csharp
    private static readonly Lazy<string> Resolved = new(() =>
    {
        var directory = new DesktopSettingsLocation().Directory;

        // One-time carry-over from the folder the app used before it was renamed. Only
        // meaningful on Windows, where that older version ran.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            CarryOverFrom(Path.Combine(appData, PreviousFolderName), directory);
        }

        return directory;
    });
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~SettingsLocation|FullyQualifiedName~PresetStore"`
Expected: PASS, including the existing carry-over tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Resolve the settings folder per platform, behind an interface"
```

---

## Task 12: Flip Core to net10.0

The proof. If anything WPF remains in Core, this will not compile.

**Files:**
- Modify: `src/TrispotQR.Core/TrispotQR.Core.csproj`
- Delete: `src/TrispotQR.Core/Rendering/StaThread.cs`
- Test: `tests/TrispotQR.Tests/CorePortabilityTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: a Core assembly with no WPF reference.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Reflection;
using TrispotQR.Core.Rendering;

namespace TrispotQR.Tests;

/// <summary>
/// Core must not depend on WPF, or it cannot run on macOS, Linux or mobile.
///
/// A test rather than a note in a document, because this is exactly the kind of constraint
/// that decays: one convenient `using System.Windows.Media` for a Point or a Color and the
/// whole port quietly regresses, on Windows, where nobody would notice.
/// </summary>
public class CorePortabilityTests
{
    private static readonly Assembly Core = typeof(QrDrawing).Assembly;

    [Theory]
    [InlineData("PresentationCore")]
    [InlineData("PresentationFramework")]
    [InlineData("WindowsBase")]
    public void Core_DoesNotReference(string assemblyName)
    {
        var referenced = Core.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.False(
            referenced.Contains(assemblyName, StringComparer.OrdinalIgnoreCase),
            $"TrispotQR.Core references {assemblyName}. Referenced: {string.Join(", ", referenced)}");
    }

    [Fact]
    public void Core_TargetsAPlatformNeutralFramework()
    {
        var target = Core.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>();

        Assert.NotNull(target);
        Assert.DoesNotContain("windows", target!.FrameworkName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoPublicApi_ExposesAWpfType()
    {
        var offenders = Core.GetExportedTypes()
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(Describe)
            .Where(d => d.Type?.Namespace?.StartsWith("System.Windows", StringComparison.Ordinal) == true)
            .Select(d => $"{d.Owner}: {d.Type!.FullName}")
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0, "WPF types on Core's public surface:\n" + string.Join("\n", offenders));
    }

    private static (string Owner, Type? Type) Describe(MemberInfo member) => member switch
    {
        PropertyInfo p => ($"{p.DeclaringType?.Name}.{p.Name}", p.PropertyType),
        FieldInfo f => ($"{f.DeclaringType?.Name}.{f.Name}", f.FieldType),
        MethodInfo m => ($"{m.DeclaringType?.Name}.{m.Name}", m.ReturnType),
        _ => (member.Name, null),
    };
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~CorePortabilityTests"`
Expected: FAIL, "TrispotQR.Core references PresentationCore" and the target framework
containing "windows".

- [ ] **Step 3: Write minimal implementation**

Replace the `PropertyGroup` in `src/TrispotQR.Core/TrispotQR.Core.csproj`:

```xml
  <PropertyGroup>
    <!--
      Plain net10.0, with no Windows suffix and no UseWPF. This is the whole point of the
      port: Core is pure logic plus SkiaSharp, so it runs unchanged on Windows, macOS,
      Linux, and later on Android and iOS.

      CorePortabilityTests asserts this stays true. Do not add a WPF reference back for a
      Point, a Rect or a Color; the equivalents live in TrispotQR.Core.Primitives.
    -->
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>TrispotQR.Core</RootNamespace>
    <Description>QR encoding, styling, geometry building, export and scannability validation.</Description>
  </PropertyGroup>
```

Delete `src/TrispotQR.Core/Rendering/StaThread.cs` and any remaining `using` that named it.
The test project keeps `net10.0-windows` and `UseWPF` while the WPF app exists.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test TrispotQR.slnx`
Expected: PASS. Compile errors here are the point: each names a Core file still reaching for
WPF. Fix by using the Core primitives, never by adding the reference back.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Target plain net10.0 in Core, with a test that keeps it that way

Core no longer references PresentationCore, PresentationFramework or
WindowsBase, and no longer exposes a WPF type on its public surface. That
is asserted rather than assumed, because one convenient using directive
would undo it on Windows, where nobody would notice."
```

---

## Task 13: Confirm the app is unchanged, and publish

**Files:**
- Modify: `README.md`, `CHANGELOG.md`

- [ ] **Step 1: Run the whole suite**

Run: `dotnet test TrispotQR.slnx`
Expected: PASS, zero skipped.

- [ ] **Step 2: Publish and launch**

```powershell
.\publish.ps1 -SkipTests
Start-Process .\dist\TrispotQR.exe
```

Expected: the app opens and looks exactly as it did before this phase.

- [ ] **Step 3: Check by hand what tests cannot**

This phase's promise is that nothing changed, so confirm each:
1. The preview updates as you type and stays crisp when the window is resized.
2. All six presets show a green badge.
3. Save a PNG with a transparent background; open it on a dark background and confirm it is genuinely transparent.
4. Save an SVG and open it in Edge; it must match the preview, corner colours and outlines included.
5. Copy to clipboard, paste into PowerPoint and into Word. Neither may show a black box.
6. Add a logo; the punch-out must still cut a clean hole and the code must still scan.
7. Open a preset saved before the port. It must load with its colours intact.
8. **Scan a saved PNG with a phone.** The synthetic decode is necessary and not sufficient.

- [ ] **Step 4: Update the documents**

In `CHANGELOG.md`, above the `1.0.0` section:

```markdown
## Unreleased

**Internal: the drawing engine no longer depends on Windows.** Rendering moved from WPF to
SkiaSharp and the geometry model became the app's own rather than WPF's. Nothing about the
app changed for anyone using it; this is the groundwork for the macOS and Linux versions.

Saved styles and settings are unaffected, and the settings folder now resolves per platform.
```

In `README.md`, under "Building from source", update the test count to match the suite.

- [ ] **Step 5: Commit and push**

```bash
git add -A
git commit -m "Phase 1 complete: Core runs without Windows"
git push -u origin phase1-core-without-wpf
```

---

## Self-review

**Spec coverage.** Core loses WPF (Tasks 1 to 5, 12). `RgbColor` (1). `QrPath` (2). Skia
conversion (3). SVG data writer (4). The switch, including the logo punch through a Skia
boolean op and the aspect ratio read without WPF (5). Rasteriser with no window (6).
Exporters and decoder on raw pixels (7). Cross-renderer proof (8). SVG export (9). Clipboard
behind an interface, mobile rule 4 (10). Storage behind an interface, mobile rule 2 (11).
`StaThread` deleted (12). Preset format preserved (1, 5). Not covered by design: the Avalonia
port is Phase 2, CI and packaging are Phase 3.

**Placeholders.** None. Every code step carries its code.

**Type consistency.** `QrLayer.Geometry` became `QrLayer.Path`, used as `Path` in Tasks 5, 6,
8, 9. `PenFor` became `StrokeFor` returning `QrStroke?` with `Thickness` fed from
`ThicknessRatio`, consistent in 5, 6, 9. `QrRenderer` became `WpfQrRenderer` taking
`RgbColor?`, consistent in 5, 7, 8. `PngExporter` and `QrDecoder` take `RasterImage` from
Task 6's definition onward. `LogoCompositor.Punch` returns `QrPath?` in 5, consumed by
`Punched` in the same task.

**Ordering.** Tasks 3 and 4 come before the `ShapeFactory` rewrite deliberately, and build
their test geometry from `QrFigure` literals, so every task leaves a compiling tree. Task 5
is large because geometry and colour both pass through `QrDrawing` and cannot be split
without committing a red tree.
