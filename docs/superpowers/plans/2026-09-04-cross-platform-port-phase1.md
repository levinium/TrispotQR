# Cross-platform port, Phase 1: Core loses WPF

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove every WPF dependency from `TrispotQR.Core` so it targets plain `net10.0`, rasterising with SkiaSharp, while the existing WPF app keeps running on top of it and all 599 tests stay green.

**Architecture:** Core gains two primitives, `RgbColor` and `QrPath`, and stops speaking WPF's `Color` and `Geometry`. `QrPath` is the single geometry model in module units; it converts to an `SKPath` for rasterising, to SVG path data for export, and (temporarily, from the app only) to a WPF `Geometry` for the existing preview. Rasterising moves to SkiaSharp, which draws offscreen with no window and behaves identically on every platform. Nothing about the UI changes in this phase.

**Tech Stack:** .NET 10, SkiaSharp 4.151.2, ZXing.Net 0.16.11, Net.Codecrete.QrCodeGenerator 3.1.0, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-04-cross-platform-port-design.md`

## Global Constraints

- **Core must not reference WPF.** No `System.Windows`, no `UseWPF`, no `net10.0-windows`. Task 15 flips the target framework, and that flip is the proof.
- **Behaviour is preserved.** This phase changes no user-visible behaviour. Any difference is a bug, except antialiasing, where pixel-identical output is explicitly not the goal and decoding correctly is.
- **The preset file format does not change.** Colours are stored as `#RRGGBB`, or `#AARRGGBB` when alpha is below 255. Existing `presets.json` and `settings.json` must keep loading.
- **The test suite stays green at every commit.** 599 tests today. Never commit red.
- **Commit style:** author is `levinium`, no `Co-Authored-By` trailer, no mention of Claude.
- **Geometry is in module units** with the quiet zone included, exactly as now. Pixel size is applied by a single scale at render time.
- **Line endings are LF.** `.gitattributes` enforces this; do not fight it.
- **Mobile rules from the spec apply from this phase onward.** Core takes bytes and streams, never paths or dialogs. Storage goes behind an interface. No desktop-only assumptions in Core.

---

## File Structure

**New in `src/TrispotQR.Core/`:**

| File | Responsibility |
|---|---|
| `Primitives/RgbColor.cs` | Colour value type, replacing `System.Windows.Media.Color`. Hex parse and format |
| `Primitives/QrPath.cs` | The neutral geometry model: points, segments, figures, fill rule |
| `Primitives/QrPathBuilder.cs` | Accumulates figures into a `QrPath` |
| `Rendering/SkiaPath.cs` | `QrPath` to `SKPath` and back |
| `Rendering/SkiaPathOps.cs` | Boolean exclude, for the logo punch-out |
| `Rendering/SkiaRasterizer.cs` | `QrDrawing` to pixels. No window, no display |
| `Export/SvgPathData.cs` | `QrPath` to an SVG `d` attribute |
| `Presets/ISettingsLocation.cs` | Where settings live, behind an interface |
| `Presets/DesktopSettingsLocation.cs` | Per-OS resolution of that folder |

**Rewritten in Core:** `Rendering/ShapeFactory.cs`, `Rendering/QrDrawing.cs`, `Rendering/QrGeometryBuilder.cs`, `Rendering/LogoCompositor.cs`, `Export/PngExporter.cs`, `Export/SvgExporter.cs`, `Validation/QrDecoder.cs`, `Validation/ScannabilityChecker.cs`, `Presets/ColorJsonConverter.cs`, `Styling/QrStyle.cs`, `Styling/StylePresets.cs`, `Styling/HsvColor.cs`.

**Deleted from Core:** `Rendering/QrRenderer.cs` (moves to the app), `Rendering/StaThread.cs` (Windows-only), `Export/ClipboardExporter.cs` (moves to the app).

**New in `src/TrispotQR.App/`:** `Rendering/WpfGeometryAdapter.cs` and `Rendering/WpfQrRenderer.cs`, both **throwaway**, deleted in Phase 2 with the rest of the WPF app. `Export/WpfClipboardExporter.cs`, which stays conceptually and is reimplemented for Avalonia in Phase 2.

---

## Task 1: RgbColor

**Files:**
- Create: `src/TrispotQR.Core/Primitives/RgbColor.cs`
- Test: `tests/TrispotQR.Tests/RgbColorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `readonly record struct RgbColor(byte A, byte R, byte G, byte B)` with statics `FromRgb(byte,byte,byte)`, `FromArgb(byte,byte,byte,byte)`, `Black`, `White`, `Transparent`; instance `ToHex()`; static `TryParse(string?, out RgbColor)` and `Parse(string)`.

- [ ] **Step 1: Write the failing test**

```csharp
using TrispotQR.Core.Primitives;

namespace TrispotQR.Tests;

public class RgbColorTests
{
    [Fact]
    public void FromRgb_IsFullyOpaque() =>
        Assert.Equal(255, RgbColor.FromRgb(1, 2, 3).A);

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

    /// <summary>Presets written before the port used WPF colour names in hand edits.</summary>
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
    public void TryParse_RejectsTheRest(string? text) =>
        Assert.False(RgbColor.TryParse(text, out _));

    [Fact]
    public void RoundTrip_SurvivesHex()
    {
        var original = RgbColor.FromArgb(0x7F, 0x10, 0x20, 0x30);
        Assert.True(RgbColor.TryParse(original.ToHex(), out var parsed));
        Assert.Equal(original, parsed);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~RgbColorTests"`
Expected: FAIL to compile, "The type or namespace name 'RgbColor' could not be found".

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Globalization;

namespace TrispotQR.Core.Primitives;

/// <summary>
/// A colour, with no dependency on any UI framework.
///
/// This replaces System.Windows.Media.Color, which is the single thing that kept most of
/// this project's styling code tied to Windows. Channel order matches the old type (A, R,
/// G, B) so the swap reads the same at every call site.
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
    public string ToHex() =>
        A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{A:X2}{R:X2}{G:X2}{B:X2}";

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

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~RgbColorTests"`
Expected: PASS, 14 tests.

- [ ] **Step 5: Commit**

```bash
git -C TrispotQR add src/TrispotQR.Core/Primitives/RgbColor.cs tests/TrispotQR.Tests/RgbColorTests.cs
git -C TrispotQR commit -m "Add RgbColor, a colour type with no UI framework behind it"
```

---

## Task 2: The QrPath geometry model

**Files:**
- Create: `src/TrispotQR.Core/Primitives/QrPath.cs`
- Create: `src/TrispotQR.Core/Primitives/QrPathBuilder.cs`
- Test: `tests/TrispotQR.Tests/QrPathTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `readonly record struct QrPoint(double X, double Y)`; `abstract record QrSegment` with `QrLineTo(QrPoint To)`, `QrArcTo(QrPoint To, double Radius, bool Clockwise)`, `QrCubicTo(QrPoint C1, QrPoint C2, QrPoint To)`; `sealed record QrFigure(QrPoint Start, IReadOnlyList<QrSegment> Segments, bool IsClosed)`; `enum QrFillRule { NonZero, EvenOdd }`; `sealed record QrPath(IReadOnlyList<QrFigure> Figures, QrFillRule FillRule)` with `static QrPath Empty` and `bool IsEmpty`; `sealed class QrPathBuilder` with `Add(QrFigure)`, `AddRange(IEnumerable<QrFigure>)`, `Build(QrFillRule)`.

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
        var path = new QrPathBuilder()
            .Add(Square(0, 0, 1))
            .Add(Square(2, 0, 1))
            .Build(QrFillRule.NonZero);

        Assert.Equal(2, path.Figures.Count);
        Assert.Equal(new QrPoint(0, 0), path.Figures[0].Start);
        Assert.Equal(new QrPoint(2, 0), path.Figures[1].Start);
        Assert.Equal(QrFillRule.NonZero, path.FillRule);
        Assert.False(path.IsEmpty);
    }

    [Fact]
    public void Builder_CarriesTheFillRule() =>
        Assert.Equal(QrFillRule.EvenOdd, new QrPathBuilder().Build(QrFillRule.EvenOdd).FillRule);

    /// <summary>
    /// Segments are compared by value, which is what lets tests assert on a shape without
    /// reaching into it.
    /// </summary>
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

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~QrPathTests"`
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

/// <summary>A straight line to <paramref name="To"/>.</summary>
public sealed record QrLineTo(QrPoint To) : QrSegment;

/// <summary>
/// A circular arc to <paramref name="To"/>. Always the small arc: every corner this app
/// draws is a quarter circle or less, so there is no large-arc flag to carry around.
/// </summary>
public sealed record QrArcTo(QrPoint To, double Radius, bool Clockwise) : QrSegment;

/// <summary>A cubic bezier. Not produced today, carried because SkiaSharp emits them.</summary>
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

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~QrPathTests"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git -C TrispotQR add src/TrispotQR.Core/Primitives/QrPath.cs src/TrispotQR.Core/Primitives/QrPathBuilder.cs tests/TrispotQR.Tests/QrPathTests.cs
git -C TrispotQR commit -m "Add QrPath, the geometry model that replaces WPF Geometry"
```

---

## Task 3: ShapeFactory emits QrFigure

**Files:**
- Modify: `src/TrispotQR.Core/Rendering/ShapeFactory.cs` (whole file)
- Test: `tests/TrispotQR.Tests/ShapeFactoryTests.cs`

**Interfaces:**
- Consumes: `QrFigure`, `QrPoint`, `QrLineTo`, `QrArcTo` from Task 2.
- Produces: `internal static class ShapeFactory` with `QrFigure RoundedRect(double x, double y, double width, double height, double topLeft, double topRight, double bottomRight, double bottomLeft)`, `QrFigure RoundedRect(double x, double y, double size, double radius)`, `QrFigure Circle(double x, double y, double size)`, `QrFigure Diamond(double x, double y, double size)`. Signatures are unchanged apart from the return type.

`ShapeFactory` is `internal`, so the test project reaches it through the `InternalsVisibleTo` already present in `TrispotQR.Core.csproj`.

- [ ] **Step 1: Write the failing test**

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
        Assert.All(figure.Segments, s => Assert.IsType<QrLineTo>(s));
        Assert.Equal(4, figure.Segments.Count);
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

        Assert.Equal(4, figure.Segments.OfType<QrArcTo>().Count());
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
    public void Offsets_ArePlacedWhereAsked()
    {
        var figure = ShapeFactory.RoundedRect(3, 7, 2, 0);
        Assert.Equal(new QrPoint(3, 7), figure.Start);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~ShapeFactoryTests"`
Expected: FAIL to compile, `PathFigure` cannot convert to `QrFigure`.

- [ ] **Step 3: Write minimal implementation**

Replace the whole of `ShapeFactory.cs`:

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

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~ShapeFactoryTests"`
Expected: PASS, 6 tests. The rest of the solution will not compile yet; that is expected and Task 6 fixes it. To see only this task's result, temporarily run with `dotnet build src/TrispotQR.Core` and note that `QrGeometryBuilder` errors are the next task's work.

**Note for the implementer:** Tasks 3 through 6 leave the solution uncompilable in between, because `QrGeometryBuilder` consumes `ShapeFactory` and produces WPF geometry. Do Tasks 3, 4, 5 and 6 as one working session and commit at the end of Task 6. Steps 5 below stages the change without committing.

- [ ] **Step 5: Stage, do not commit yet**

```bash
git -C TrispotQR add src/TrispotQR.Core/Rendering/ShapeFactory.cs tests/TrispotQR.Tests/ShapeFactoryTests.cs
```

---

## Task 4: QrPath to SKPath and back

**Files:**
- Modify: `src/TrispotQR.Core/TrispotQR.Core.csproj` (add SkiaSharp)
- Create: `src/TrispotQR.Core/Rendering/SkiaPath.cs`
- Test: `tests/TrispotQR.Tests/SkiaPathTests.cs`

**Interfaces:**
- Consumes: `QrPath`, `QrFigure`, `QrPoint`, `QrLineTo`, `QrArcTo`, `QrCubicTo`, `QrFillRule`.
- Produces: `internal static class SkiaPath` with `SKPath ToSKPath(QrPath path)` and `QrPath ToQrPath(SKPath path, QrFillRule fillRule)`.

- [ ] **Step 1: Write the failing test**

```csharp
using SkiaSharp;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Rendering;

namespace TrispotQR.Tests;

public class SkiaPathTests
{
    private static QrPath Square(double x, double y, double size) =>
        new QrPathBuilder()
            .Add(ShapeFactory.RoundedRect(x, y, size, 0))
            .Build(QrFillRule.NonZero);

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
        Assert.Equal(SKPathFillType.Winding, nonZero.FillType);

        var evenOdd = new QrPathBuilder().Add(ShapeFactory.RoundedRect(0, 0, 1, 0)).Build(QrFillRule.EvenOdd);
        using var sk = SkiaPath.ToSKPath(evenOdd);
        Assert.Equal(SKPathFillType.EvenOdd, sk.FillType);
    }

    [Fact]
    public void ARoundedShape_SurvivesTheTrip()
    {
        var original = new QrPathBuilder().Add(ShapeFactory.Circle(0, 0, 8)).Build(QrFillRule.NonZero);
        using var sk = SkiaPath.ToSKPath(original);

        Assert.Equal(0f, sk.Bounds.Left, 2);
        Assert.Equal(8f, sk.Bounds.Right, 2);
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

        using var again = SkiaPath.ToSKPath(back);
        Assert.Equal(sk.Bounds.Left, again.Bounds.Left, 3);
        Assert.Equal(sk.Bounds.Right, again.Bounds.Right, 3);
        Assert.NotEmpty(back.Figures);
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

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~SkiaPathTests"`
Expected: FAIL to compile, "The type or namespace name 'SkiaSharp' could not be found".

- [ ] **Step 3: Write minimal implementation**

Add to `src/TrispotQR.Core/TrispotQR.Core.csproj`, inside the existing `PackageReference` `ItemGroup`:

```xml
    <PackageReference Include="SkiaSharp" Version="4.151.2" />
    <!--
      The native binaries are per-platform and are not pulled in by the main package for
      anything but the host. Naming all three keeps a self-contained publish for any RID
      working, which Phase 3 depends on.
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
                        // Always the small arc: every corner this app draws is a quarter
                        // circle or less.
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
    /// Reads a Skia path back into the model. Skia has already flattened arcs into cubics
    /// by this point, which is why <see cref="QrCubicTo"/> exists: the model has to be able
    /// to express whatever comes back or the punch-out would lose its rounded corners.
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
                    // Raised to a cubic so the model needs only one curve type.
                    segments.Add(QuadToCubic(Point(points[0]), Point(points[1]), Point(points[2])));
                    break;

                case SKPathVerb.Cubic:
                    segments.Add(new QrCubicTo(Point(points[1]), Point(points[2]), Point(points[3])));
                    break;

                case SKPathVerb.Conic:
                    // Skia uses conics for true circular arcs. Approximated as a cubic,
                    // which is what every consumer of this path can draw.
                    segments.Add(QuadToCubic(Point(points[0]), Point(points[1]), Point(points[2])));
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

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~SkiaPathTests"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Stage, do not commit yet**

```bash
git -C TrispotQR add src/TrispotQR.Core/TrispotQR.Core.csproj src/TrispotQR.Core/Rendering/SkiaPath.cs tests/TrispotQR.Tests/SkiaPathTests.cs
```

---

## Task 5: SVG path data from QrPath

**Files:**
- Create: `src/TrispotQR.Core/Export/SvgPathData.cs`
- Test: `tests/TrispotQR.Tests/SvgPathDataTests.cs`

**Interfaces:**
- Consumes: `QrPath`, `QrFigure`, segment records, `QrFillRule`.
- Produces: `internal static class SvgPathData` with `string ToData(QrPath path)` and `string FillRule(QrFillRule rule)`.

This replaces the trick of stripping WPF's `F0`/`F1` prefix off `Geometry.ToString()`, which was a WPF-specific debug-string behaviour that would not have survived the port.

- [ ] **Step 1: Write the failing test**

```csharp
using TrispotQR.Core.Export;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Rendering;

namespace TrispotQR.Tests;

public class SvgPathDataTests
{
    [Fact]
    public void ASharpSquare_IsMoveLinesAndClose()
    {
        var path = new QrPathBuilder().Add(ShapeFactory.RoundedRect(0, 0, 2, 0)).Build(QrFillRule.NonZero);

        Assert.Equal("M0,0 L2,0 L2,2 L0,2 L0,0 Z", SvgPathData.ToData(path));
    }

    [Fact]
    public void AnArc_UsesTheSvgArcCommand()
    {
        var path = new QrPathBuilder().Add(ShapeFactory.RoundedRect(0, 0, 4, 1)).Build(QrFillRule.NonZero);
        var data = SvgPathData.ToData(path);

        // A 1-unit radius, no rotation, small arc, clockwise sweep.
        Assert.Contains("A1,1 0 0 1 ", data);
    }

    [Fact]
    public void Numbers_AreInvariantAndTrimmed()
    {
        var figure = new QrFigure(new QrPoint(1.5, 2.25), [new QrLineTo(new QrPoint(3.0, 4.123456))], IsClosed: false);
        var path = new QrPathBuilder().Add(figure).Build(QrFillRule.NonZero);

        Assert.Equal("M1.5,2.25 L3,4.1235", SvgPathData.ToData(path));
    }

    [Fact]
    public void AnEmptyPath_ProducesNothing() =>
        Assert.Equal(string.Empty, SvgPathData.ToData(QrPath.Empty));

    [Fact]
    public void FillRule_MapsToTheSvgKeywords()
    {
        Assert.Equal("nonzero", SvgPathData.FillRule(QrFillRule.NonZero));
        Assert.Equal("evenodd", SvgPathData.FillRule(QrFillRule.EvenOdd));
    }

    [Fact]
    public void MultipleFigures_AreSeparatedBySpaces()
    {
        var path = new QrPathBuilder()
            .Add(ShapeFactory.RoundedRect(0, 0, 1, 0))
            .Add(ShapeFactory.RoundedRect(2, 0, 1, 0))
            .Build(QrFillRule.NonZero);

        Assert.Equal(2, SvgPathData.ToData(path).Split('M', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void ACubic_UsesTheCurveCommand()
    {
        var figure = new QrFigure(
            new QrPoint(0, 0),
            [new QrCubicTo(new QrPoint(1, 0), new QrPoint(2, 1), new QrPoint(2, 2))],
            IsClosed: false);

        Assert.Equal("M0,0 C1,0 2,1 2,2", SvgPathData.ToData(new QrPathBuilder().Add(figure).Build(QrFillRule.NonZero)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~SvgPathDataTests"`
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
/// The previous version of this took WPF's path mini-language from Geometry.ToString and
/// stripped its leading fill-rule token. That worked, and it depended on a debug-string
/// format of a Windows-only type. Writing the data is a dozen lines and owes nothing to
/// anything.
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
                        // rx,ry x-rotation large-arc-flag sweep-flag x,y
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

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~SvgPathDataTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Stage, do not commit yet**

```bash
git -C TrispotQR add src/TrispotQR.Core/Export/SvgPathData.cs tests/TrispotQR.Tests/SvgPathDataTests.cs
```

---

## Task 6: QrDrawing, QrGeometryBuilder and LogoCompositor move to QrPath and RgbColor

This is the largest task and the one that makes the solution compile again. It ends with a commit.

**Files:**
- Modify: `src/TrispotQR.Core/Rendering/QrDrawing.cs` (whole file)
- Create: `src/TrispotQR.Core/Rendering/SkiaPathOps.cs`
- Modify: `src/TrispotQR.Core/Rendering/QrGeometryBuilder.cs` (geometry types throughout)
- Modify: `src/TrispotQR.Core/Rendering/LogoCompositor.cs` (geometry types)
- Modify: `src/TrispotQR.Core/Styling/QrStyle.cs`, `Styling/StylePresets.cs`, `Styling/HsvColor.cs`, `Presets/ColorJsonConverter.cs` (`Color` becomes `RgbColor`)
- Create: `src/TrispotQR.App/Rendering/WpfGeometryAdapter.cs`
- Create: `src/TrispotQR.App/Rendering/WpfQrRenderer.cs`
- Modify: every app and test file that referenced `System.Windows.Media.Color` for a style
- Test: `tests/TrispotQR.Tests/SkiaPathOpsTests.cs`

**Interfaces:**
- Consumes: `RgbColor`, `QrPath`, `SkiaPath`, `ShapeFactory`.
- Produces:
  - `sealed record QrStroke(RgbColor Color, double Thickness)`
  - `sealed class QrLayer(string name, QrPath path, RgbColor fill, QrStroke? stroke)` with properties `Name`, `Path`, `Fill`, `Stroke`
  - `sealed class QrDrawing(double sizeInUnits, RgbColor? background, IReadOnlyList<QrLayer> layers, LogoPlacement? logo)`
  - `internal static class SkiaPathOps` with `QrPath Exclude(QrPath subject, QrPath punch)`
  - App-side: `static Geometry WpfGeometryAdapter.ToGeometry(QrPath)`, `static Color WpfGeometryAdapter.ToColor(RgbColor)`, and `WpfQrRenderer` with the three methods `QrRenderer` had.

- [ ] **Step 1: Write the failing test for the boolean op**

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

        Assert.True(result.Figures.Count >= 2, "expected an outer outline and a hole");

        using var sk = SkiaPath.ToSKPath(result);
        Assert.Equal(0f, sk.Bounds.Left, 3);
        Assert.Equal(10f, sk.Bounds.Right, 3);

        // The centre of the punched area is no longer painted.
        Assert.False(sk.Contains(5f, 5f));
        Assert.True(sk.Contains(1f, 1f));
    }

    [Fact]
    public void Exclude_LeavesTheSubjectAloneWhenNothingOverlaps()
    {
        var result = SkiaPathOps.Exclude(Square(0, 0, 2), Square(50, 50, 2));

        using var sk = SkiaPath.ToSKPath(result);
        Assert.Equal(0f, sk.Bounds.Left, 3);
        Assert.Equal(2f, sk.Bounds.Right, 3);
        Assert.True(sk.Contains(1f, 1f));
    }

    [Fact]
    public void Exclude_OfAnEmptySubjectIsEmpty() =>
        Assert.True(SkiaPathOps.Exclude(QrPath.Empty, Square(0, 0, 1)).IsEmpty);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~SkiaPathOpsTests"`
Expected: FAIL to compile, "The name 'SkiaPathOps' does not exist".

- [ ] **Step 3a: Implement SkiaPathOps**

```csharp
using SkiaSharp;
using TrispotQR.Core.Primitives;

namespace TrispotQR.Core.Rendering;

/// <summary>
/// Boolean operations on paths, which is the one piece of geometry work the model cannot
/// do for itself. Used only for the logo punch-out, and only when a logo is present.
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

- [ ] **Step 3b: Rewrite QrDrawing.cs**

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

/// <summary>An outline drawn around a layer, in module units.</summary>
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

- [ ] **Step 3c: Update QrGeometryBuilder**

Mechanical throughout. The three changes that matter:

```csharp
// Was: using System.Windows.Media;
using TrispotQR.Core.Primitives;

// Layer construction: a PathGeometry becomes a QrPathBuilder.
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

// The punch: Geometry.Combine becomes the Skia op.
private static QrLayer Punched(QrLayer layer, QrPath punch) =>
    new(layer.Name, SkiaPathOps.Exclude(layer.Path, punch), layer.Fill, layer.Stroke);
```

Replace the old `PenFor` helper with `StrokeFor`, returning `QrStroke?`:

```csharp
/// <summary>The outline for a layer, or null when this style does not outline it.</summary>
private static QrStroke? StrokeFor(QrStyle style, OutlineTarget target)
{
    var outline = style.Outline;

    if (!outline.Enabled || !outline.Applies(target))
    {
        return null;
    }

    return new QrStroke(outline.Color, outline.Thickness);
}
```

Apply the same `QrPathBuilder` change to `BuildMarkerFrameLayer` and `BuildMarkerCenterLayer`, and delete the `Brush`, `Normalise` and `PenFor` helpers along with the `Freeze` calls, which have no equivalent and are not needed: `QrPath` is immutable by construction.

- [ ] **Step 3d: Update LogoCompositor**

`Punch` returns `QrPath?` instead of `Geometry?`, built from `ShapeFactory` exactly as before:

```csharp
using TrispotQR.Core.Primitives;

/// <summary>The clear area cut out from under the logo, or null when the style wants none.</summary>
public static QrPath? Punch(LogoPlacement logo, QrStyle style)
{
    if (style.Logo.PunchOut == LogoPunch.None)
    {
        return null;
    }

    var pad = style.Logo.Padding;
    var x = logo.X - pad;
    var y = logo.Y - pad;
    var width = logo.Width + (pad * 2);
    var height = logo.Height + (pad * 2);

    var figure = style.Logo.PunchOut switch
    {
        LogoPunch.Circle => ShapeFactory.Circle(x, y, Math.Max(width, height)),
        LogoPunch.RoundedSquare => ShapeFactory.RoundedRect(x, y, width, height, 0.4, 0.4, 0.4, 0.4),
        _ => ShapeFactory.RoundedRect(x, y, width, height, 0, 0, 0, 0),
    };

    return new QrPathBuilder().Add(figure).Build(QrFillRule.NonZero);
}
```

- [ ] **Step 3e: Swap Color for RgbColor across Styling and Presets**

In `QrStyle.cs`, `StylePresets.cs`, `HsvColor.cs` and `ColorJsonConverter.cs`, delete `using System.Windows.Media;`, add `using TrispotQR.Core.Primitives;`, and replace every `Color` with `RgbColor`. Constructors change from `Color.FromRgb(r, g, b)` to `RgbColor.FromRgb(r, g, b)` and `Colors.Black` to `RgbColor.Black`.

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

- [ ] **Step 3f: Add the throwaway WPF adapter in the app**

`src/TrispotQR.App/Rendering/WpfGeometryAdapter.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using TrispotQR.Core.Primitives;

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

- [ ] **Step 3g: Move QrRenderer into the app as WpfQrRenderer**

Move `src/TrispotQR.Core/Rendering/QrRenderer.cs` to `src/TrispotQR.App/Rendering/WpfQrRenderer.cs`. Rename the class to `WpfQrRenderer`, change the namespace to `TrispotQR.App.Rendering`, and convert at the point of use:

```csharp
foreach (var layer in drawing.Layers)
{
    context.DrawGeometry(
        WpfGeometryAdapter.ToBrush(layer.Fill),
        WpfGeometryAdapter.ToPen(layer.Stroke),
        WpfGeometryAdapter.ToGeometry(layer.Path));
}
```

Background handling becomes `WpfGeometryAdapter.ToColor` on the nullable `RgbColor`. Update every reference across the app and tests from `QrRenderer` to `WpfQrRenderer`, and add `using TrispotQR.App.Rendering;`.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test TrispotQR/TrispotQR.slnx`
Expected: PASS, 599 existing tests plus the new ones from Tasks 1 to 6, so 622 or more. Fix every compile error in tests by swapping `Color` for `RgbColor` and `QrRenderer` for `WpfQrRenderer`. Do not change any assertion's meaning.

- [ ] **Step 5: Commit**

```bash
git -C TrispotQR add -A
git -C TrispotQR commit -m "Move Core's geometry and colour onto its own types

QrPath replaces WPF Geometry and RgbColor replaces WPF Color throughout
Core. The WPF app keeps rendering through a throwaway adapter that Phase 2
deletes with the rest of the window.

The logo punch-out now goes through a Skia boolean op rather than
Geometry.Combine, and SVG path data is written from the model instead of
being scraped out of Geometry.ToString."
```

---

## Task 7: The Skia rasteriser

**Files:**
- Create: `src/TrispotQR.Core/Rendering/SkiaRasterizer.cs`
- Test: `tests/TrispotQR.Tests/SkiaRasterizerTests.cs`

**Interfaces:**
- Consumes: `QrDrawing`, `QrLayer`, `RgbColor`, `SkiaPath`.
- Produces: `public sealed record RasterImage(int Width, int Height, byte[] Pixels)` where `Pixels` is BGRA, 4 bytes per pixel, premultiplied; `public static class SkiaRasterizer` with `RasterImage Render(QrDrawing drawing, int pixelSize, RgbColor? backgroundOverride = null)` and `byte[] EncodePng(RasterImage image)`.

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

    private static (byte B, byte G, byte R, byte A) PixelAt(RasterImage image, int x, int y)
    {
        var i = ((y * image.Width) + x) * 4;
        return (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2], image.Pixels[i + 3]);
    }

    [Fact]
    public void Render_ProducesTheRequestedSize()
    {
        var image = SkiaRasterizer.Render(Build(), 256);

        Assert.Equal(256, image.Width);
        Assert.Equal(256, image.Height);
        Assert.Equal(256 * 256 * 4, image.Pixels.Length);
    }

    [Fact]
    public void AWhiteBackground_IsOpaque()
    {
        var image = SkiaRasterizer.Render(Build(QrStyle.Default with { Background = RgbColor.White }), 128);

        Assert.Equal(255, PixelAt(image, 2, 2).A);
    }

    /// <summary>
    /// The quiet zone of a transparent code must have a genuinely empty alpha channel, not
    /// white pixels. This is the property the whole transparent-PNG feature rests on.
    /// </summary>
    [Fact]
    public void NoBackground_LeavesTheQuietZoneTransparent()
    {
        var image = SkiaRasterizer.Render(Build(QrStyle.Default with { Background = null }), 128);

        Assert.Equal(0, PixelAt(image, 2, 2).A);
    }

    [Fact]
    public void BackgroundOverride_FlattensATransparentCode()
    {
        var image = SkiaRasterizer.Render(
            Build(QrStyle.Default with { Background = null }), 128, RgbColor.White);

        Assert.Equal(255, PixelAt(image, 2, 2).A);
    }

    [Fact]
    public void EncodePng_ProducesARealPngHeader()
    {
        var bytes = SkiaRasterizer.EncodePng(SkiaRasterizer.Render(Build(), 64));

        Assert.Equal([0x89, 0x50, 0x4E, 0x47], bytes.Take(4));
    }

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

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~SkiaRasterizerTests"`
Expected: FAIL to compile, "The name 'SkiaRasterizer' does not exist".

- [ ] **Step 3: Write minimal implementation**

```csharp
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

        var scale = (float)(pixelSize / drawing.SizeInUnits);
        canvas.Save();
        canvas.Scale(scale);

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
            System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        }

        return new RasterImage(pixelSize, pixelSize, pixels);
    }

    public static byte[] EncodePng(RasterImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap();
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(image.Pixels, System.Runtime.InteropServices.GCHandleType.Pinned);

        try
        {
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

        var target = new SKRect((float)logo.X, (float)logo.Y, (float)(logo.X + logo.Width), (float)(logo.Y + logo.Height));
        canvas.DrawBitmap(bitmap, target);
    }

    private static SKColor ToSk(RgbColor c) => new(c.R, c.G, c.B, c.A);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~SkiaRasterizerTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git -C TrispotQR add src/TrispotQR.Core/Rendering/SkiaRasterizer.cs tests/TrispotQR.Tests/SkiaRasterizerTests.cs
git -C TrispotQR commit -m "Rasterise with Skia, off-screen and with no UI framework"
```

---

## Task 8: PngExporter and QrDecoder on raw pixels

**Files:**
- Modify: `src/TrispotQR.Core/Export/PngExporter.cs` (whole file)
- Modify: `src/TrispotQR.Core/Validation/QrDecoder.cs` (whole file)
- Test: `tests/TrispotQR.Tests/PngExporterTests.cs` (new), and update `tests/TrispotQR.Tests/RenderAndDecodeTests.cs`

**Interfaces:**
- Consumes: `RasterImage`, `SkiaRasterizer`.
- Produces: `PngExporter.Save(RasterImage image, string path)`, `PngExporter.ToBytes(RasterImage image)`; `QrDecoder.Decode(RasterImage image)`, `QrDecoder.DecodeStrict(RasterImage image)`.

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
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"trispotqr-png-{Guid.NewGuid():N}");

    public PngExporterTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static RasterImage Render(RgbColor? background, int size = 256)
    {
        var encoded = QrEncoder.Encode("https://example.org/tickets", EccLevel.Medium);
        var drawing = QrGeometryBuilder.Build(encoded.Matrix!, QrStyle.Default with { Background = background });
        return SkiaRasterizer.Render(drawing, size);
    }

    [Fact]
    public void Save_WritesAFileThatDecodesBack()
    {
        var path = Path.Combine(_directory, "code.png");
        PngExporter.Save(Render(RgbColor.White), path);

        Assert.True(File.Exists(path));
        Assert.Equal("https://example.org/tickets", QrDecoder.Decode(Render(RgbColor.White)));
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

    /// <summary>A code saved with no background must stay genuinely transparent on disk.</summary>
    [Fact]
    public void ATransparentCode_KeepsItsAlphaChannel()
    {
        var image = Render(background: null);
        var corner = image.Pixels[3];

        Assert.Equal(0, corner);
    }

    [Fact]
    public void ATransparentCode_StillDecodesBecauseDecodingFlattensOntoWhite() =>
        Assert.Equal("https://example.org/tickets", QrDecoder.Decode(Render(background: null)));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~PngExporterTests"`
Expected: FAIL to compile, `RasterImage` cannot convert to `BitmapSource`.

- [ ] **Step 3a: Rewrite PngExporter**

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

- [ ] **Step 3b: Rewrite QrDecoder**

Keep the whole two-pass doc comment from the current file verbatim; only the input type and the flattening change.

```csharp
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Rendering;
using ZXing;

namespace TrispotQR.Core.Validation;

/// <summary>
/// Reads a QR code back out of rendered pixels. This is what turns "the style looks fine"
/// into "the style actually scans", and it is the check the whole styling feature rests on.
/// </summary>
public static class QrDecoder
{
    /// <summary>
    /// Decodes the image, or returns null when no code could be read. Any alpha is
    /// flattened onto white first, matching what a camera would see on paper or a screen.
    ///
    /// Two decoder passes, because neither alone is right.
    ///
    /// The camera-like pass locates the code by hunting for its corner markers, the way a
    /// phone does. It is the more meaningful test, but it is also fussy on a clean
    /// synthetic render: measured over 300 plain black-on-white codes it wrongly failed 15
    /// of them, all perfectly valid. Reporting "does not scan" on a plain QR code destroys
    /// any trust in the badge, so a failure there is not taken as final.
    ///
    /// The pure pass reads the module grid directly, which suits an image we rendered
    /// ourselves. It misread none of those 300. It is not simply permissive either: it
    /// still refuses a code with its corner markers painted out, or with half of it erased.
    ///
    /// So a code counts as readable if either pass reads it, and the real-world risks a
    /// clean render cannot show, contrast, inversion, quiet zone, logo coverage, are
    /// checked explicitly elsewhere rather than being inferred from a decode failure.
    /// </summary>
    public static string? Decode(RasterImage image) =>
        DecodeStrict(image) ?? Read(image, pureBarcode: true);

    /// <summary>
    /// Decodes using only the camera-like pass, which locates the code by its corner
    /// markers. Stricter than <see cref="Decode"/> and prone to false failures on clean
    /// renders, so it is meant for the test suite, where every style shipped in the app is
    /// held to the higher bar, rather than for judging a user's own content.
    /// </summary>
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
    /// compositing over white is simply adding the uncovered remainder.
    /// </summary>
    private static byte[] FlattenToBgr24(RasterImage image)
    {
        var result = new byte[image.Width * image.Height * 3];

        for (int source = 0, target = 0; source < image.Pixels.Length; source += 4, target += 3)
        {
            var alpha = image.Pixels[source + 3];
            var uncovered = 255 - alpha;

            result[target] = (byte)(image.Pixels[source] + uncovered);
            result[target + 1] = (byte)(image.Pixels[source + 1] + uncovered);
            result[target + 2] = (byte)(image.Pixels[source + 2] + uncovered);
        }

        return result;
    }
}
```

- [ ] **Step 3c: Update every call site**

`ScannabilityChecker`, `RenderAndDecodeTests`, `StyleMatrixScanTests`, `ScannabilityFalseAlarmTests`, `LogoTests`, `SvgExporterTests`, `MainViewModel` and `ExportGuardTests` all render then decode. Replace `QrRenderer.RenderToBitmap(drawing, size)` with `SkiaRasterizer.Render(drawing, size)` and `PngExporter.Save(bitmap, path)` with `PngExporter.Save(image, path)`. In `ScannabilityChecker`, delete the `StaThread.Run` wrapper: Skia needs no apartment thread.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test TrispotQR/TrispotQR.slnx`
Expected: PASS. The style matrix is the one to watch; if a shape stops decoding, the fault is in `SkiaPath` arc conversion, not in the geometry.

- [ ] **Step 5: Commit**

```bash
git -C TrispotQR add -A
git -C TrispotQR commit -m "Export and decode from raw pixels rather than a WPF bitmap"
```

---

## Task 9: Prove the Skia renderer is faithful

The single most valuable test in this phase. It runs only while both renderers exist, so it must be written now and is deleted in Phase 2 with the WPF app.

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
/// This is the guard for the whole port. Pixel-identical output is explicitly not the
/// goal, because two engines antialias differently, and it would fail for reasons nobody
/// should care about. What matters is that a code rendered the new way still scans, and
/// still carries the same content, for every style the app can produce.
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

        var wpf = _host.Run(() =>
        {
            var bitmap = WpfQrRenderer.RenderToBitmap(drawing, 512, RgbColor.White);
            return DecodeWpf(bitmap);
        });

        Assert.Equal(Payload, wpf);
        Assert.Equal(Payload, skia);
    }

    /// <summary>
    /// Every shape combination, not just the presets. This is the same guard
    /// StyleMatrixScanTests applies, pointed at the question of whether the renderer swap
    /// changed anything.
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

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~RendererEquivalenceTests"`
Expected: PASS. If a combination fails only under Skia, the fault is almost certainly the arc conversion in `SkiaPath.ToSKPath`: check that `SKPathArcSize.Small` and the sweep direction match what `ShapeFactory` intended.

- [ ] **Step 3: Commit**

```bash
git -C TrispotQR add tests/TrispotQR.Tests/RendererEquivalenceTests.cs
git -C TrispotQR commit -m "Prove the Skia renderer reads back the same as the WPF one"
```

---

## Task 10: SvgExporter on the model

**Files:**
- Modify: `src/TrispotQR.Core/Export/SvgExporter.cs`
- Modify: `tests/TrispotQR.Tests/SvgExporterTests.cs`

**Interfaces:**
- Consumes: `SvgPathData`, `QrDrawing`, `RgbColor`.
- Produces: unchanged public surface, `SvgExporter.Save(QrDrawing, int, string)` and `SvgExporter.ToSvg(QrDrawing, int)`.

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

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~SvgExporterTests"`
Expected: FAIL to compile, `PathData(Geometry)` no longer applies because `QrLayer.Geometry` is gone.

- [ ] **Step 3: Write minimal implementation**

In `SvgExporter.cs`, delete `using System.Windows.Media;`, add `using TrispotQR.Core.Primitives;`, and delete the whole `PathData` method with its comment about WPF's mini-language. Replace `AppendLayer` with:

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
```

Change the two helpers to take `RgbColor`:

```csharp
    private static string Hex(RgbColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>SVG carries alpha separately, so a partly transparent colour needs the extra attribute.</summary>
    private static string Opacity(RgbColor color) =>
        color.A == 255 ? string.Empty : $@" fill-opacity=""{(color.A / 255.0).ToString("0.###", Invariant)}""";
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~SvgExporter"`
Expected: PASS, the existing tests plus the two new ones.

- [ ] **Step 5: Commit**

```bash
git -C TrispotQR add src/TrispotQR.Core/Export/SvgExporter.cs tests/TrispotQR.Tests/SvgExporterTests.cs
git -C TrispotQR commit -m "Write SVG path data from the model instead of scraping WPF"
```

---

## Task 11: Clipboard export moves to the app

Clipboard access is inherently a platform service and belongs behind an interface, per the spec's mobile rules.

**Files:**
- Create: `src/TrispotQR.Core/Export/IImageClipboard.cs`
- Delete: `src/TrispotQR.Core/Export/ClipboardExporter.cs`
- Create: `src/TrispotQR.App/Export/WpfImageClipboard.cs` (the old file's body, taking bytes)
- Modify: `src/TrispotQR.App/ViewModels/MainViewModel.cs` to take `IImageClipboard`
- Modify: `tests/TrispotQR.Tests/ClipboardExporterTests.cs`

**Interfaces:**
- Consumes: `RasterImage`, `PngExporter`.
- Produces: `public interface IImageClipboard { void Copy(RasterImage image); }`; `WpfImageClipboard : IImageClipboard`.

- [ ] **Step 1: Write the failing test**

Rename `ClipboardExporterTests` to target the new type, keeping every existing assertion and adding:

```csharp
    [Fact]
    public void TheInterface_IsWhatTheViewModelDependsOn()
    {
        // Core must not know how a clipboard works on any particular platform.
        var contract = typeof(TrispotQR.Core.Export.IImageClipboard);

        Assert.True(contract.IsInterface);
        Assert.Single(contract.GetMethods());
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~Clipboard"`
Expected: FAIL to compile, `IImageClipboard` does not exist.

- [ ] **Step 3: Write minimal implementation**

`src/TrispotQR.Core/Export/IImageClipboard.cs`:

```csharp
using TrispotQR.Core.Rendering;

namespace TrispotQR.Core.Export;

/// <summary>
/// Puts an image on the system clipboard.
///
/// An interface because every platform advertises image formats differently, and because
/// Core must stay free of anything that assumes a desktop. The Windows implementation
/// lives in the app; Phase 2 adds an Avalonia one beside it.
/// </summary>
public interface IImageClipboard
{
    /// <summary>Copies the image, throwing if the clipboard could not be written.</summary>
    void Copy(RasterImage image);
}
```

Move the body of the old `ClipboardExporter` into `src/TrispotQR.App/Export/WpfImageClipboard.cs` as `public sealed class WpfImageClipboard : IImageClipboard`. Keep the whole comment about not disposing the `MemoryStream`, which is a real bug this project already paid for once, and build the WPF `BitmapSource` it needs from the PNG bytes:

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
        data.SetImage(FlattenOntoWhite(image));

        SetWithRetry(data);
        VerifyLanded();
    }
```

`FlattenOntoWhite` decodes the PNG bytes into a `BitmapImage` and composites onto white exactly as the old code did.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~Clipboard"`
Expected: PASS. These tests touch the real clipboard and are already in the non-parallel `UI` collection; leave them there.

- [ ] **Step 5: Commit**

```bash
git -C TrispotQR add -A
git -C TrispotQR commit -m "Put the clipboard behind an interface and move Windows code to the app"
```

---

## Task 12: Settings location behind an interface

**Files:**
- Create: `src/TrispotQR.Core/Presets/ISettingsLocation.cs`
- Create: `src/TrispotQR.Core/Presets/DesktopSettingsLocation.cs`
- Modify: `src/TrispotQR.Core/Presets/PresetStore.cs`
- Test: `tests/TrispotQR.Tests/SettingsLocationTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public interface ISettingsLocation { string Directory { get; } }`; `public sealed class DesktopSettingsLocation : ISettingsLocation` with `static string ResolveFor(OSPlatform platform, string home, string? appData, string? xdgConfigHome)`.

- [ ] **Step 1: Write the failing test**

```csharp
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

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~SettingsLocationTests"`
Expected: FAIL to compile, `DesktopSettingsLocation` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.IO;
using System.Runtime.InteropServices;

namespace TrispotQR.Core.Presets;

/// <summary>
/// Where saved styles and settings live.
///
/// An interface because the answer is different on every platform, and different again on
/// mobile, where the app writes into its own sandbox. Core asks; it does not decide.
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

In `PresetStore`, replace the `Resolved` lazy's body so it asks the location and then carries over:

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

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~SettingsLocation"`
Expected: PASS, 5 tests. The existing `PresetStoreTests` carry-over tests must still pass.

- [ ] **Step 5: Commit**

```bash
git -C TrispotQR add -A
git -C TrispotQR commit -m "Resolve the settings folder per platform, behind an interface"
```

---

## Task 13: Flip Core to net10.0

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

Run: `dotnet test TrispotQR/TrispotQR.slnx --filter "FullyQualifiedName~CorePortabilityTests"`
Expected: FAIL, "TrispotQR.Core references PresentationCore" and "target framework contains windows", because the csproj still says so.

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

Delete `src/TrispotQR.Core/Rendering/StaThread.cs`. Apartment threading is a Windows concept and nothing in a Skia pipeline needs it. Delete the `StaThread.Run` wrappers from any remaining caller and the `using` lines that referenced it.

The test project keeps `net10.0-windows` and `UseWPF` while the WPF app exists, so it can still test both.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test TrispotQR/TrispotQR.slnx`
Expected: PASS, everything. Compile errors here are the point: each one names a file in Core still reaching for WPF. Fix by using the Core primitives, never by adding the reference back.

- [ ] **Step 5: Commit**

```bash
git -C TrispotQR add -A
git -C TrispotQR commit -m "Target plain net10.0 in Core, with a test that keeps it that way

Core no longer references PresentationCore, PresentationFramework or
WindowsBase, and no longer exposes a WPF type on its public surface. That
is asserted rather than assumed, because one convenient using directive
would undo it on Windows, where nobody would notice."
```

---

## Task 14: Confirm the app is unchanged, and publish

**Files:**
- Modify: `README.md`, `CHANGELOG.md`

**Interfaces:**
- Consumes: everything.
- Produces: a published Windows build that behaves as it did before Phase 1.

- [ ] **Step 1: Run the whole suite**

Run: `dotnet test TrispotQR/TrispotQR.slnx`
Expected: PASS, roughly 650 tests, zero skipped.

- [ ] **Step 2: Publish and launch**

```powershell
& "TrispotQR\publish.ps1" -SkipTests
Start-Process "TrispotQR\dist\TrispotQR.exe"
```

Expected: the app opens and looks exactly as it did before this phase.

- [ ] **Step 3: Check by hand what tests cannot**

Confirm each, since this phase's promise is that nothing changed:
1. The preview updates as you type, and stays crisp when the window is resized.
2. Every one of the six presets shows a green badge.
3. Save PNG with a transparent background, then open the file on a dark background and confirm it is genuinely transparent.
4. Save SVG and open it in Edge; it must match the preview, corner colours and outlines included.
5. Copy to clipboard, then paste into PowerPoint and into Word. Neither may show a black box.
6. Add a logo, confirm the punch-out still cuts a clean hole and the code still scans.
7. Open an existing saved preset from before the port. It must load with its colours intact.
8. **Scan a saved PNG with a phone.** The synthetic decode is necessary and not sufficient.

- [ ] **Step 4: Update the documents**

In `CHANGELOG.md`, add above the `1.0.0` section:

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
git -C TrispotQR add -A
git -C TrispotQR commit -m "Phase 1 complete: Core runs without Windows"
git -C TrispotQR push
```

---

## Self-review

**Spec coverage.** Core loses WPF (Tasks 1 to 6, 13). `RgbColor` (1). `QrPath` (2, 3). SkiaSharp rasteriser with no window (7). Exporters rewritten (8, 10). Decoder on raw bytes (8). `StaThread` deleted (13). The `SvgExporter` `ToString` trick retired (5, 10). Preset format preserved (1, 6). Storage behind an interface, mobile rule 2 (12). Clipboard behind an interface, mobile rule 4 (11). Cross-renderer decode test (9). Phase 1 ends with the WPF app running on a throwaway adapter (6). Not covered here by design: the Avalonia port is Phase 2, and CI and packaging are Phase 3.

**Placeholders.** None. Every code step carries the code.

**Type consistency.** `QrLayer.Geometry` became `QrLayer.Path` and every consumer named in Tasks 6, 7, 9 and 10 uses `Path`. `PenFor` became `StrokeFor` returning `QrStroke?`, used consistently in Tasks 6, 7 and 10. `QrRenderer` became `WpfQrRenderer` in Tasks 6, 9 and 11. `PngExporter` and `QrDecoder` take `RasterImage` from Task 8 onward, matching Task 7's definition.

**One risk worth naming for the implementer.** `SkiaPath.ToQrPath` is only used by the logo punch-out. If Task 6's `SkiaPathOpsTests` pass but a logo renders wrongly later, look there first: Skia returns conics for circular arcs and the conversion approximates them as cubics.
