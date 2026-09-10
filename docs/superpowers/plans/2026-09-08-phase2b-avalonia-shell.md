# Phase 2b: Avalonia shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a running Avalonia desktop app on top of the already-extracted `TrispotQR.ViewModels`, alongside the existing WPF app, whose spine works end to end: pick a content type, type into it, watch a live vector preview, read the scannability badge, save a PNG or an SVG.

**Architecture:** Two new projects. `TrispotQR.UI` is an Avalonia class library holding the views, controls, converters and platform services; `TrispotQR.Desktop` is a thin entry point that owns the per-RID publish. Both consume the existing `MainViewModel` unchanged — if any task finds itself editing `MainViewModel`, that is a signal to stop and report, not to proceed. The preview draws `QrPath` into an Avalonia `StreamGeometry` so the on-screen code stays vector-crisp; export continues to go through Core's Skia rasteriser and never through the UI.

**Tech Stack:** Avalonia 12.1.2 (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Headless.XUnit`), .NET 10, SkiaSharp 4.151.2 (Core's pin, which the whole graph unifies to), xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-09-04-cross-platform-port-design.md`

## Global Constraints

- Target framework for every new project is `net10.0`. No `-windows` suffix anywhere in this phase.
- Avalonia version is exactly `12.1.2` for all Avalonia packages. Do not float it.
- SkiaSharp stays at `4.151.2`. Avalonia 12.1.2 declares a dependency on SkiaSharp `3.119.4` and NuGet unifies the graph up to `4.151.2`. This was verified to work by spike: Avalonia's Skia backend rendered text, a `Border` and a real `QrPath` while Core rasterised and decoded a QR in the same process. **Do not "fix" this by downgrading Core** — Core does not compile against 3.119.4 (`SKPathBuilder` does not exist, and two `SKCanvas` overloads differ). If a version warning appears, report it rather than changing a version.
- The existing WPF app (`src/TrispotQR.App`) keeps building and passing throughout. This phase adds; it removes nothing.
- `MainViewModel`, `IDialogService`, `IUiTimer` and `IImageClipboard` are consumed exactly as they are. Changing them is out of scope.
- Compiled bindings are on (`AvaloniaUseCompiledBindingsByDefault=true`), so every XAML file with bindings declares `x:DataType`. An unresolvable binding becomes a build error, which is the point.
- House test style: test names are sentences describing behaviour (`SavesThePngWhereTheDialogSaid`), and comments explain *why* a non-obvious thing is done rather than restating the line.
- No `Co-Authored-By` trailer on commits in this repository.

## What is NOT in this phase

Listed so no task quietly grows to include it. All of these stay in the WPF app for now and land in Phase 2c: the styling panel (module and marker shapes, module scale, outline, quiet zone, ECC), the colour picker, the preset strip and its thumbnails, the settings window, light/dark theming, the logo picker, and the About and confirmation dialogs. Retiring the WPF project is Phase 2d.

Packaging is also out: this phase produces something that runs from `dotnet run`, not a
`.dmg`, an AppImage or a single-file `.exe`. Those come once the Avalonia build reaches parity.

Content editors are the one deliberate partial: Task 5 ships templates for plain text and link only, and the remaining five content types render a short placeholder saying their fields arrive in the next phase.

## File Structure

```
src/TrispotQR.UI/                          Avalonia class library, net10.0
  TrispotQR.UI.csproj
  App.axaml, App.axaml.cs                  Application, FluentTheme, composition root
  MainWindow.axaml, MainWindow.axaml.cs    The shell window
  Rendering/AvaloniaGeometry.cs            QrPath -> StreamGeometry, RgbColor -> Color/Brush/Pen
  Controls/QrPreview.cs                    Custom control that draws a whole QrDrawing
  Converters/UiConverters.cs               Scan verdict -> brush
  Services/AvaloniaUiTimer.cs              IUiTimer via Avalonia's DispatcherTimer
  Services/AvaloniaImageClipboard.cs       IImageClipboard via TopLevel.Clipboard
  Services/AvaloniaDialogService.cs        IDialogService via IStorageProvider
  Services/FileFilter.cs                   Parses the Win32 filter string the interface carries

src/TrispotQR.Desktop/                     Entry point, net10.0
  TrispotQR.Desktop.csproj
  Program.cs

tests/TrispotQR.UI.Tests/                  Avalonia.Headless.XUnit, net10.0, all three OSes
  TrispotQR.UI.Tests.csproj
  TestAppBuilder.cs                        [assembly: AvaloniaTestApplication]
  AvaloniaGeometryTests.cs
  QrPreviewTests.cs
  FileFilterTests.cs
  ServiceTests.cs
  MainWindowTests.cs
```

Why `TrispotQR.UI` rather than `TrispotQR.App`: the WPF project owns that name during coexistence. Keeping views in a library rather than folding them into the entry point also preserves the spec's mobile rule — a future Android or iOS head references the same library and supplies its own entry point. Whether to rename it once WPF is gone is a Phase 2d decision.

---

### Task 1: The UI project, geometry conversion, and proof on three operating systems

The riskiest assumption in this phase is that Avalonia and Core's SkiaSharp coexist on Linux and macOS, where the native `libSkiaSharp` also has to load. This task turns that assumption into a CI result before anything is built on top of it.

**Files:**
- Create: `src/TrispotQR.UI/TrispotQR.UI.csproj`
- Create: `src/TrispotQR.UI/App.axaml`, `src/TrispotQR.UI/App.axaml.cs`
- Create: `src/TrispotQR.UI/Rendering/AvaloniaGeometry.cs`
- Create: `tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`
- Create: `tests/TrispotQR.UI.Tests/TestAppBuilder.cs`
- Test: `tests/TrispotQR.UI.Tests/AvaloniaGeometryTests.cs`
- Modify: `TrispotQR.slnx`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `TrispotQR.Core.Primitives.{QrPath, QrFigure, QrLineTo, QrArcTo, QrCubicTo, QrFillRule, QrPoint, RgbColor}`; `TrispotQR.Core.Rendering.{QrGeometryBuilder.Build(QrMatrix, QrStyle), SkiaRasterizer.Render(QrDrawing, int, RgbColor?), QrStroke}`; `TrispotQR.Core.Qr.QrEncoder.Encode(string, EccLevel)`; `TrispotQR.Core.Validation.QrDecoder.Decode(RasterImage)`; `TrispotQR.Core.Styling.StylePresets.BuiltIn`.
- Produces: `TrispotQR.UI.App` (an `Avalonia.Application`), and `TrispotQR.UI.Rendering.AvaloniaGeometry` with `public static StreamGeometry ToStreamGeometry(QrPath path)`, `public static Color ToColor(RgbColor c)`, `public static IBrush ToBrush(RgbColor c)`, `public static IPen? ToPen(QrStroke? stroke)`.

- [ ] **Step 1: Create the UI project and its Application class**

`src/TrispotQR.UI/TrispotQR.UI.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!-- Plain net10.0. The whole point of this project is that it is not Windows-only. -->
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="12.1.2" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\TrispotQR.ViewModels\TrispotQR.ViewModels.csproj" />
  </ItemGroup>

</Project>
```

`src/TrispotQR.UI/App.axaml`:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="TrispotQR.UI.App"
             RequestedThemeVariant="Default">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```

`src/TrispotQR.UI/App.axaml.cs`:

```csharp
using Avalonia;
using Avalonia.Markup.Xaml;

namespace TrispotQR.UI;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 2: Write the failing geometry tests**

`tests/TrispotQR.UI.Tests/AvaloniaGeometryTests.cs`:

```csharp
using Avalonia;
using TrispotQR.Core.Primitives;
using TrispotQR.UI.Rendering;

namespace TrispotQR.UI.Tests;

public class AvaloniaGeometryTests
{
    [Fact]
    public void AnEmptyPathBecomesAnEmptyGeometry()
    {
        var geometry = AvaloniaGeometry.ToStreamGeometry(QrPath.Empty);

        Assert.Equal(0, geometry.Bounds.Width);
        Assert.Equal(0, geometry.Bounds.Height);
    }

    [Fact]
    public void ARectangularFigureKeepsItsBounds()
    {
        var rectangle = new QrPath(
            [
                new QrFigure(
                    new QrPoint(1, 1),
                    [
                        new QrLineTo(new QrPoint(3, 1)),
                        new QrLineTo(new QrPoint(3, 4)),
                        new QrLineTo(new QrPoint(1, 4)),
                    ],
                    IsClosed: true),
            ],
            QrFillRule.NonZero);

        var geometry = AvaloniaGeometry.ToStreamGeometry(rectangle);

        Assert.Equal(1, geometry.Bounds.X, 3);
        Assert.Equal(1, geometry.Bounds.Y, 3);
        Assert.Equal(2, geometry.Bounds.Width, 3);
        Assert.Equal(3, geometry.Bounds.Height, 3);
    }

    [Fact]
    public void TheFillRuleCarriesOver()
    {
        // A ring: an outer square with an inner square inside it, both wound the same way.
        // Under EvenOdd the middle is a hole. Getting this wrong is how a marker centre
        // ends up a solid block, so it is asserted rather than assumed.
        var ring = new QrPath(
            [
                new QrFigure(
                    new QrPoint(0, 0),
                    [new QrLineTo(new QrPoint(6, 0)), new QrLineTo(new QrPoint(6, 6)), new QrLineTo(new QrPoint(0, 6))],
                    IsClosed: true),
                new QrFigure(
                    new QrPoint(2, 2),
                    [new QrLineTo(new QrPoint(4, 2)), new QrLineTo(new QrPoint(4, 4)), new QrLineTo(new QrPoint(2, 4))],
                    IsClosed: true),
            ],
            QrFillRule.EvenOdd);

        var geometry = AvaloniaGeometry.ToStreamGeometry(ring);

        Assert.False(geometry.FillContains(new Point(3, 3)));
        Assert.True(geometry.FillContains(new Point(1, 1)));
    }
}
```

- [ ] **Step 3: Create the test project and run the tests to watch them fail**

`tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!-- Runs in the portable CI job on Windows, Linux and macOS. -->
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia.Headless.XUnit" Version="12.1.2" />
    <PackageReference Include="Avalonia.Skia" Version="12.1.2" />
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\TrispotQR.UI\TrispotQR.UI.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

`tests/TrispotQR.UI.Tests/TestAppBuilder.cs`:

```csharp
using Avalonia;
using Avalonia.Headless;
using TrispotQR.UI;
using TrispotQR.UI.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace TrispotQR.UI.Tests;

/// <summary>
/// Wires the headless platform for every [AvaloniaFact] in this assembly.
///
/// UseHeadlessDrawing is false deliberately. The default headless mode stubs out drawing
/// entirely, which would let a preview test pass without Skia ever running. Turning it off
/// makes Avalonia render through its real Skia backend, which is the thing worth proving
/// given Core pins a different SkiaSharp major than Avalonia was built against.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
```

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`
Expected: FAIL — `AvaloniaGeometry` does not exist.

- [ ] **Step 4: Implement AvaloniaGeometry**

`src/TrispotQR.UI/Rendering/AvaloniaGeometry.cs`:

```csharp
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Rendering;

namespace TrispotQR.UI.Rendering;

/// <summary>
/// Converts the app's own geometry and colour types into Avalonia's.
///
/// The mirror image of the WPF adapter, and unlike that one this is not throwaway: it is how
/// every platform draws the live preview from here on. Export never comes through here. That
/// stays on Core's Skia rasteriser, which is what keeps a saved PNG identical across
/// operating systems rather than at the mercy of each toolkit's antialiasing.
/// </summary>
public static class AvaloniaGeometry
{
    public static Color ToColor(RgbColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    public static IBrush ToBrush(RgbColor c) => new ImmutableSolidColorBrush(ToColor(c));

    public static IPen? ToPen(QrStroke? stroke) =>
        stroke is null
            ? null
            : new ImmutablePen(
                new ImmutableSolidColorBrush(ToColor(stroke.Color)),
                stroke.Thickness,
                lineJoin: PenLineJoin.Round);

    public static StreamGeometry ToStreamGeometry(QrPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var geometry = new StreamGeometry();
        using var context = geometry.Open();

        context.SetFillRule(path.FillRule == QrFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero);

        foreach (var figure in path.Figures)
        {
            context.BeginFigure(ToPoint(figure.Start), isFilled: true);

            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case QrLineTo line:
                        context.LineTo(ToPoint(line.To));
                        break;

                    case QrArcTo arc:
                        // Always the small arc and never rotated: every corner this app draws
                        // is a quarter circle, which is what QrArcTo documents.
                        context.ArcTo(
                            ToPoint(arc.To),
                            new Size(arc.Radius, arc.Radius),
                            rotationAngle: 0,
                            isLargeArc: false,
                            arc.Clockwise ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                        break;

                    case QrCubicTo cubic:
                        context.CubicBezierTo(ToPoint(cubic.C1), ToPoint(cubic.C2), ToPoint(cubic.To));
                        break;

                    default:
                        throw new NotSupportedException($"Unhandled segment type {segment.GetType().Name}.");
                }
            }

            context.EndFigure(figure.IsClosed);
        }

        return geometry;
    }

    private static Point ToPoint(QrPoint p) => new(p.X, p.Y);
}
```

- [ ] **Step 5: Run the geometry tests and watch them pass**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`
Expected: PASS, 3 tests.

- [ ] **Step 6: Write the coexistence test, which is the actual point of this task**

Append to `tests/TrispotQR.UI.Tests/AvaloniaGeometryTests.cs`, adding the usings `Avalonia.Controls`, `Avalonia.Headless.XUnit`, `Avalonia.Media`, `TrispotQR.Core.Qr`, `TrispotQR.Core.Rendering`, `TrispotQR.Core.Styling` and `TrispotQR.Core.Validation`:

```csharp
public class SkiaCoexistenceTests
{
    [AvaloniaFact]
    public void AvaloniaRendersWhileCoreRasterisesAndDecodes()
    {
        // Avalonia 12.1.2 is compiled against SkiaSharp 3.119.4; Core pins 4.151.2 and NuGet
        // unifies the graph up to it. That was proven by hand on Windows. This test exists so
        // it is also proven on Linux and macOS, where the native libSkiaSharp has to load as
        // well, and so it stays proven whenever either version moves.
        const string payload = "https://www.example.org";

        var encoded = QrEncoder.Encode(payload, EccLevel.Medium);
        Assert.True(encoded.Success);

        var drawing = QrGeometryBuilder.Build(encoded.Matrix!, StylePresets.BuiltIn[0].Style);
        var raster = SkiaRasterizer.Render(drawing, 512);

        Assert.Equal(payload, QrDecoder.Decode(raster));

        var window = new Window { Width = 300, Height = 300 };
        window.Content = new Avalonia.Controls.Shapes.Path
        {
            Data = AvaloniaGeometry.ToStreamGeometry(drawing.Layers[0].Path),
            Fill = Brushes.Black,
        };
        window.Show();

        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.Equal(new PixelSize(300, 300), frame!.PixelSize);
    }
}
```

- [ ] **Step 7: Run it and watch it pass on Windows**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`
Expected: PASS, 4 tests.

If `CaptureRenderedFrame` returns null the headless platform fell back to stub drawing — check `UseHeadlessDrawing = false` in `TestAppBuilder`. Do not work around a null frame by weakening the assertion: a null frame means Skia never rendered, which is the exact thing this test exists to catch.

- [ ] **Step 8: Add both projects to the solution and to the CI job**

```bash
dotnet sln TrispotQR.slnx add src/TrispotQR.UI/TrispotQR.UI.csproj
dotnet sln TrispotQR.slnx add tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj
```

In `.github/workflows/ci.yml`, inside the `portable` job, immediately after the "Test view models" step:

```yaml
      - name: Test the Avalonia UI
        run: dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj -c Release --verbosity normal
```

Linux runners need no extra packages for headless Skia rendering. If the Linux job fails for want of a display, that is a real finding — report it rather than adding `xvfb`, because the headless platform is meant to need no display and needing one would mean it is misconfigured.

- [ ] **Step 9: Run the whole solution's tests**

Run: `dotnet test TrispotQR.slnx`
Expected: PASS — 415 Core + 59 view models + 220 app + 4 UI = 698.

- [ ] **Step 10: Commit, push, and report CI for all three operating systems**

```bash
git add src/TrispotQR.UI tests/TrispotQR.UI.Tests TrispotQR.slnx .github/workflows/ci.yml
git commit -m "Add the Avalonia UI project and prove it coexists with Core's SkiaSharp"
git push
```

Report the run's per-job conclusions. **This task is not complete until the ubuntu and macOS jobs have both passed with the new test in them.** A Windows-only pass proves nothing here; proving the Unix case is the entire reason this task is first.

---

### Task 2: The QrPreview control

**Files:**
- Create: `src/TrispotQR.UI/Controls/QrPreview.cs`
- Test: `tests/TrispotQR.UI.Tests/QrPreviewTests.cs`

**Interfaces:**
- Consumes: `AvaloniaGeometry.ToStreamGeometry`, `.ToBrush`, `.ToPen` from Task 1; `TrispotQR.Core.Rendering.QrDrawing` with `SizeInUnits`, `Background`, `Layers`; `QrLayer` with `Path`, `Fill`, `Stroke`.
- Produces: `TrispotQR.UI.Controls.QrPreview`, a `Control` with `public static readonly StyledProperty<QrDrawing?> DrawingProperty` and `public QrDrawing? Drawing { get; set; }`.

The logo is deliberately not drawn by this control in this phase. `QrDrawing.Logo` names a file on disk, and loading it belongs with the logo picker in Phase 2c. Exported files are unaffected: Core's rasteriser draws the logo, so a saved PNG has it either way.

- [ ] **Step 1: Write the failing tests**

`tests/TrispotQR.UI.Tests/QrPreviewTests.cs`:

```csharp
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.UI.Controls;

namespace TrispotQR.UI.Tests;

public class QrPreviewTests
{
    private static QrDrawing BuildDrawing()
    {
        var encoded = QrEncoder.Encode("https://www.example.org", EccLevel.Medium);
        return QrGeometryBuilder.Build(encoded.Matrix!, StylePresets.BuiltIn[0].Style);
    }

    private static WriteableBitmap Render(QrDrawing? drawing)
    {
        var preview = new QrPreview { Drawing = drawing };
        var window = new Window { Width = 200, Height = 200, Content = preview };
        window.Show();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        return frame!;
    }

    [AvaloniaFact]
    public void DrawsSomethingWhenGivenADrawing()
    {
        Assert.True(
            HasMoreThanOneColour(Render(BuildDrawing())),
            "the preview came out a single flat colour, so nothing was drawn");
    }

    [AvaloniaFact]
    public void DrawsNothingWhenTheDrawingIsNull()
    {
        Assert.False(HasMoreThanOneColour(Render(null)), "an empty preview should be blank");
    }

    [AvaloniaFact]
    public void ScalesTheDrawingUpToFillTheControl()
    {
        // The drawing is measured in module units, roughly 29 across for this payload, while
        // the control is 200 pixels. Without the scale transform the code would render as a
        // speck in the top-left, so this asserts that ink reaches the bottom quarter.
        Assert.True(
            HasInkBelow(Render(BuildDrawing()), 0.75),
            "no ink in the bottom quarter, so the drawing was not scaled to the control");
    }

    private static bool HasMoreThanOneColour(WriteableBitmap bitmap)
    {
        var pixels = ReadPixels(bitmap, out _);
        var first = BitConverter.ToUInt32(pixels, 0);

        for (var i = 4; i + 4 <= pixels.Length; i += 4)
        {
            if (BitConverter.ToUInt32(pixels, i) != first)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasInkBelow(WriteableBitmap bitmap, double fraction)
    {
        var pixels = ReadPixels(bitmap, out var size);
        var background = BitConverter.ToUInt32(pixels, 0);
        var stride = pixels.Length / size.Height;

        for (var y = (int)(size.Height * fraction); y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                if (BitConverter.ToUInt32(pixels, (y * stride) + (x * 4)) != background)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static byte[] ReadPixels(WriteableBitmap bitmap, out PixelSize size)
    {
        size = bitmap.PixelSize;
        using var buffer = bitmap.Lock();
        var bytes = new byte[buffer.RowBytes * size.Height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return bytes;
    }
}
```

- [ ] **Step 2: Run them to watch them fail**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`
Expected: FAIL — `QrPreview` does not exist.

- [ ] **Step 3: Implement the control**

`src/TrispotQR.UI/Controls/QrPreview.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TrispotQR.Core.Rendering;
using TrispotQR.UI.Rendering;

namespace TrispotQR.UI.Controls;

/// <summary>
/// Draws a <see cref="QrDrawing"/> as vector geometry, scaled to fill the control.
///
/// Vector rather than a rendered bitmap, so the preview stays crisp as the window resizes and
/// so resizing costs nothing in Core. What gets saved does not come through here: export goes
/// through Core's Skia rasteriser.
///
/// The logo is not drawn here yet. It lives on disk as a path in QrDrawing.Logo, and loading
/// it belongs with the logo picker in Phase 2c. Exports already include it.
/// </summary>
public class QrPreview : Control
{
    public static readonly StyledProperty<QrDrawing?> DrawingProperty =
        AvaloniaProperty.Register<QrPreview, QrDrawing?>(nameof(Drawing));

    static QrPreview() => AffectsRender<QrPreview>(DrawingProperty);

    public QrDrawing? Drawing
    {
        get => GetValue(DrawingProperty);
        set => SetValue(DrawingProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Drawing is not { } drawing || drawing.SizeInUnits <= 0)
        {
            return;
        }

        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0)
        {
            return;
        }

        // Square and centred: a QR code is square, and letting it stretch to a non-square
        // control would break the module grid a scanner relies on.
        var scale = side / drawing.SizeInUnits;
        using var _ = context.PushTransform(
            Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation((Bounds.Width - side) / 2, (Bounds.Height - side) / 2));

        if (drawing.Background is { A: > 0 } background)
        {
            context.FillRectangle(
                AvaloniaGeometry.ToBrush(background),
                new Rect(0, 0, drawing.SizeInUnits, drawing.SizeInUnits));
        }

        foreach (var layer in drawing.Layers)
        {
            if (layer.Path.IsEmpty)
            {
                continue;
            }

            context.DrawGeometry(
                AvaloniaGeometry.ToBrush(layer.Fill),
                AvaloniaGeometry.ToPen(layer.Stroke),
                AvaloniaGeometry.ToStreamGeometry(layer.Path));
        }
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/TrispotQR.UI/Controls/QrPreview.cs tests/TrispotQR.UI.Tests/QrPreviewTests.cs
git commit -m "Add the Avalonia QrPreview control"
```

---

### Task 3: The file filter parser

`IDialogService.AskForSavePath` carries a Win32 filter string such as `"PNG image|*.png"`, because the interface was extracted from a WPF app. Avalonia's picker wants `FilePickerFileType` objects instead. Parsing is a pure function, so it gets tested on its own before any dialog exists.

This is a known piece of debt: the neutral view model project should not be carrying Win32 filter syntax at all. Changing the interface is out of scope for this phase — the parser absorbs it, and a comment records why.

**Files:**
- Create: `src/TrispotQR.UI/Services/FileFilter.cs`
- Test: `tests/TrispotQR.UI.Tests/FileFilterTests.cs`

**Interfaces:**
- Produces: `TrispotQR.UI.Services.FileFilter` with `public static IReadOnlyList<FilePickerFileType> Parse(string filter)`.

- [ ] **Step 1: Write the failing tests**

`tests/TrispotQR.UI.Tests/FileFilterTests.cs`:

```csharp
using TrispotQR.UI.Services;

namespace TrispotQR.UI.Tests;

public class FileFilterTests
{
    [Fact]
    public void ReadsASingleNameAndPatternPair()
    {
        var types = FileFilter.Parse("PNG image|*.png");

        var type = Assert.Single(types);
        Assert.Equal("PNG image", type.Name);
        Assert.Equal(["*.png"], type.Patterns);
    }

    [Fact]
    public void ReadsSeveralPairs()
    {
        // The exact string the logo picker passes today.
        var types = FileFilter.Parse(
            "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp|PNG (best, supports transparency)|*.png|All files|*.*");

        Assert.Equal(3, types.Count);
        Assert.Equal("Images", types[0].Name);
        Assert.Equal(["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp"], types[0].Patterns);
        Assert.Equal("All files", types[2].Name);
        Assert.Equal(["*.*"], types[2].Patterns);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no separator at all")]
    public void AMalformedFilterYieldsNothingRatherThanThrowing(string filter)
    {
        // A picker with no file types still opens and still saves. Throwing here would turn a
        // cosmetic problem into the save button doing nothing, which is far worse.
        Assert.Empty(FileFilter.Parse(filter));
    }
}
```

- [ ] **Step 2: Run them to watch them fail**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj --filter FileFilterTests`
Expected: FAIL — `FileFilter` does not exist.

- [ ] **Step 3: Implement the parser**

`src/TrispotQR.UI/Services/FileFilter.cs`:

```csharp
using Avalonia.Platform.Storage;

namespace TrispotQR.UI.Services;

/// <summary>
/// Turns the Win32 filter string that <see cref="TrispotQR.ViewModels.IDialogService"/> carries
/// into Avalonia file types.
///
/// The interface was extracted from a WPF app and still speaks "Name|*.ext|Name|*.ext". That is
/// debt in a project meant to be toolkit-neutral, and the right fix is a structured type on the
/// interface — but changing the interface is Phase 2c work at the earliest, and absorbing the
/// format here costs one small, well-tested function in the meantime.
/// </summary>
public static class FileFilter
{
    public static IReadOnlyList<FilePickerFileType> Parse(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return [];
        }

        var parts = filter.Split('|', StringSplitOptions.TrimEntries);
        var types = new List<FilePickerFileType>(parts.Length / 2);

        // Pairs, so a trailing unpaired name is ignored rather than throwing.
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            types.Add(new FilePickerFileType(parts[i])
            {
                Patterns = [.. parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            });
        }

        return types;
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`
Expected: PASS, 12 tests.

- [ ] **Step 5: Commit**

```bash
git add src/TrispotQR.UI/Services/FileFilter.cs tests/TrispotQR.UI.Tests/FileFilterTests.cs
git commit -m "Parse the Win32 filter string into Avalonia file types"
```

---

### Task 4: The timer and clipboard services

**Files:**
- Create: `src/TrispotQR.UI/Services/AvaloniaUiTimer.cs`
- Create: `src/TrispotQR.UI/Services/AvaloniaImageClipboard.cs`
- Test: `tests/TrispotQR.UI.Tests/ServiceTests.cs`

**Interfaces:**
- Consumes: `TrispotQR.ViewModels.IUiTimer` (`TimeSpan Interval { get; set; }`, `event EventHandler Tick`, `void Start()`, `void Stop()`); `TrispotQR.Core.Export.IImageClipboard` (`void Copy(RasterImage image)`); `TrispotQR.Core.Rendering.RasterImage(int Width, int Height, byte[] Pixels)`, whose pixels are BGRA8888 premultiplied.
- Produces: `TrispotQR.UI.Services.AvaloniaUiTimer` (parameterless constructor) and `TrispotQR.UI.Services.AvaloniaImageClipboard(TopLevel topLevel)`.

**The clipboard has one genuine problem worth stating up front.** `IImageClipboard.Copy` is synchronous, but Avalonia's clipboard API is `Task SetDataObjectAsync(...)`, and the call has to happen on the UI thread. Blocking that thread on the task can deadlock. The resolution here is to pump the dispatcher while waiting, so the UI thread keeps servicing the work the task depends on, with a timeout so a wedged clipboard surfaces as an error rather than a hang. The view model already catches exceptions from `Copy` and reports them, so a thrown timeout lands in the existing error path. The cleaner long-term fix is an async clipboard interface; that is deliberately not this phase.

- [ ] **Step 1: Write the failing timer tests**

`tests/TrispotQR.UI.Tests/ServiceTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TrispotQR.Core.Rendering;
using TrispotQR.UI.Services;

namespace TrispotQR.UI.Tests;

public class AvaloniaUiTimerTests
{
    [AvaloniaFact]
    public void RemembersTheIntervalItIsGiven()
    {
        var timer = new AvaloniaUiTimer { Interval = TimeSpan.FromMilliseconds(150) };

        Assert.Equal(TimeSpan.FromMilliseconds(150), timer.Interval);
    }

    [AvaloniaFact]
    public void RaisesTickAfterTheIntervalElapses()
    {
        var ticks = 0;
        var timer = new AvaloniaUiTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => ticks++;

        timer.Start();
        Thread.Sleep(50);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        timer.Stop();

        Assert.True(ticks > 0, "the timer never ticked");
    }

    [AvaloniaFact]
    public void StopsTicking()
    {
        var timer = new AvaloniaUiTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Start();
        timer.Stop();

        var ticksAfterStopping = 0;
        timer.Tick += (_, _) => ticksAfterStopping++;
        Thread.Sleep(50);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, ticksAfterStopping);
    }
}

public class AvaloniaImageClipboardTests
{
    [AvaloniaFact]
    public void CopiesAnImageWithoutThrowing()
    {
        var window = new Window { Width = 100, Height = 100 };
        window.Show();

        var image = new RasterImage(2, 2, new byte[2 * 2 * 4]);

        // The headless clipboard accepts data and hands it back, so this exercises the real
        // sync-over-async bridge rather than mocking it away. A deadlock here fails as a
        // timeout, which is the failure mode worth catching.
        new AvaloniaImageClipboard(window).Copy(image);
    }

    [AvaloniaFact]
    public void RefusesANullImage()
    {
        var window = new Window { Width = 100, Height = 100 };
        window.Show();

        Assert.Throws<ArgumentNullException>(() => new AvaloniaImageClipboard(window).Copy(null!));
    }
}
```

- [ ] **Step 2: Run them to watch them fail**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj --filter "AvaloniaUiTimerTests|AvaloniaImageClipboardTests"`
Expected: FAIL — neither service exists.

- [ ] **Step 3: Implement the timer**

`src/TrispotQR.UI/Services/AvaloniaUiTimer.cs`:

```csharp
using Avalonia.Threading;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Services;

/// <summary>Avalonia's dispatcher timer behind the shared interface.</summary>
public sealed class AvaloniaUiTimer : IUiTimer
{
    private readonly DispatcherTimer _timer = new();

    public AvaloniaUiTimer() => _timer.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public event EventHandler? Tick;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();
}
```

- [ ] **Step 4: Implement the clipboard**

`src/TrispotQR.UI/Services/AvaloniaImageClipboard.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using TrispotQR.Core.Export;
using TrispotQR.Core.Rendering;

namespace TrispotQR.UI.Services;

/// <summary>
/// Puts a rendered code on the system clipboard as PNG bytes.
///
/// PNG rather than a raw bitmap because it keeps transparency, which is the whole reason the
/// app offers a transparent background. Avalonia has no per-platform bitmap clipboard format
/// the way WPF does, so unlike the WPF implementation there is no second flattened-on-white
/// entry; whether that is needed on each platform is a question for Phase 2c, when the
/// Avalonia build is actually pasted into Word and PowerPoint.
/// </summary>
public sealed class AvaloniaImageClipboard(TopLevel topLevel) : IImageClipboard
{
    /// <summary>How long to wait for the clipboard before calling it stuck.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly TopLevel _topLevel = topLevel ?? throw new ArgumentNullException(nameof(topLevel));

    public void Copy(RasterImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var clipboard = _topLevel.Clipboard
            ?? throw new InvalidOperationException("This window has no clipboard.");

        var data = new DataObject();
        data.Set("PNG", PngExporter.ToBytes(image));

        // Sync over async, deliberately. IImageClipboard.Copy is synchronous because the view
        // model's copy command is, and the clipboard call must run on the UI thread. Waiting on
        // the task outright would deadlock, since the work it needs is queued on this very
        // thread, so the dispatcher is pumped while waiting. The timeout turns a wedged
        // clipboard into an exception the view model already knows how to report, rather than a
        // frozen window.
        var task = clipboard.SetDataObjectAsync(data);
        var deadline = DateTime.UtcNow + Timeout;

        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The clipboard did not respond.");
            }

            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        // Unwraps into the original exception rather than an AggregateException.
        task.GetAwaiter().GetResult();
    }
}
```

- [ ] **Step 5: Run the tests and watch them pass**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`
Expected: PASS, 17 tests.

If `CopiesAnImageWithoutThrowing` times out, do not raise the timeout to make it pass. A timeout means the pump is not servicing the clipboard's work, and the fix is in how it is pumped — report it.

- [ ] **Step 6: Commit**

```bash
git add src/TrispotQR.UI/Services tests/TrispotQR.UI.Tests/ServiceTests.cs
git commit -m "Add the Avalonia timer and clipboard services"
```

---

### Task 5: The dialog service

`MainViewModel`'s save path calls exactly three members of `IDialogService`: `ConfirmRisk` (only when the code did not scan cleanly), `AskForSavePath`, and `ShowError` when the write fails. Those three plus `Confirm` and `ShowInformation` are implemented here for real. `AskForImage`, `AskForText` and `EditSettings` belong to the logo picker, the save-preset flow and the settings window, all of which are Phase 2c, so they return null — which the view model already reads as "the user cancelled" — with a comment saying so.

Avalonia's pickers and dialogs are async, and so is the clipboard from Task 4, so the waiting logic is extracted into one helper both use.

**Files:**
- Create: `src/TrispotQR.UI/Services/DispatcherWait.cs`
- Create: `src/TrispotQR.UI/Views/MessageWindow.axaml`, `src/TrispotQR.UI/Views/MessageWindow.axaml.cs`
- Create: `src/TrispotQR.UI/Services/AvaloniaDialogService.cs`
- Modify: `src/TrispotQR.UI/Services/AvaloniaImageClipboard.cs`
- Test: `tests/TrispotQR.UI.Tests/DialogServiceTests.cs`

**Interfaces:**
- Consumes: `FileFilter.Parse` from Task 3; `TrispotQR.ViewModels.IDialogService`; `TrispotQR.Core.Presets.AppSettings`.
- Produces: `TrispotQR.UI.Services.DispatcherWait` with `public static T For<T>(Task<T> task, TimeSpan timeout)` and `public static void For(Task task, TimeSpan timeout)`; `TrispotQR.UI.Views.MessageWindow` with `public static Task<bool> ShowAsync(Window owner, string title, string message, string confirmLabel, string? cancelLabel, bool defaultToConfirm)`; `TrispotQR.UI.Services.AvaloniaDialogService(Window owner)`.

- [ ] **Step 1: Extract the dispatcher wait and point the clipboard at it**

`src/TrispotQR.UI/Services/DispatcherWait.cs`:

```csharp
using Avalonia.Threading;

namespace TrispotQR.UI.Services;

/// <summary>
/// Waits for an Avalonia task from the UI thread without deadlocking.
///
/// Every interesting thing Avalonia offers a desktop app is async — the clipboard, the file
/// picker, a modal dialog — while IDialogService and IImageClipboard are synchronous, because
/// they were extracted from a WPF app where those calls block. Waiting on such a task outright
/// deadlocks: the work it is waiting for is queued on the very thread that is blocked. Pumping
/// the dispatcher lets that queued work run, and the timeout turns a wedged dialog into an
/// exception the view model already reports rather than a frozen window.
///
/// The real fix is asynchronous interfaces. That is a deliberate later change, because it
/// reaches into Core, the view models and the WPF app, none of which this phase touches.
/// </summary>
public static class DispatcherWait
{
    public static void For(Task task, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(task);
        Pump(task, timeout);
        task.GetAwaiter().GetResult();
    }

    public static T For<T>(Task<T> task, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(task);
        Pump(task, timeout);
        return task.GetAwaiter().GetResult();
    }

    private static void Pump(Task task, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The window did not respond in time.");
            }

            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
    }
}
```

Then replace the inline pump in `AvaloniaImageClipboard.Copy` with a call to it, keeping the five-second timeout and leaving the explanatory comment about PNG-only clipboard data in place:

```csharp
        DispatcherWait.For(clipboard.SetDataObjectAsync(data), Timeout);
```

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`
Expected: PASS, still 17 tests. The clipboard tests from Task 4 are what prove the extraction did not change behaviour.

- [ ] **Step 2: Write the failing dialog service tests**

`tests/TrispotQR.UI.Tests/DialogServiceTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TrispotQR.Core.Presets;
using TrispotQR.UI.Services;

namespace TrispotQR.UI.Tests;

public class DialogServiceTests
{
    private static (Window Owner, AvaloniaDialogService Dialogs) Create()
    {
        var window = new Window { Width = 400, Height = 300 };
        window.Show();
        return (window, new AvaloniaDialogService(window));
    }

    [AvaloniaFact]
    public void AskingForASavePathReturnsNullWhenThereIsNoPicker()
    {
        var (_, dialogs) = Create();

        // The headless platform has no file picker, so this cannot assert on a chosen path.
        // What it does prove is the part that actually goes wrong: the call reaches the storage
        // provider, comes back, and does not deadlock the dispatcher on the way. If the
        // sync-over-async bridge were broken this would hang until DispatcherWait's timeout and
        // fail, which is exactly the signal wanted.
        var path = dialogs.AskForSavePath("Save QR code as PNG", "PNG image|*.png", ".png", "code.png", null);

        Assert.Null(path);
    }

    [AvaloniaFact]
    public void TheThreePhase2cMembersReportCancellationRatherThanCrashing()
    {
        var (_, dialogs) = Create();

        // The logo picker, the save-preset prompt and the settings window arrive in Phase 2c.
        // Until then these must behave like a cancelled dialog, because that is the one answer
        // every caller in MainViewModel already handles.
        Assert.Null(dialogs.AskForImage(null));
        Assert.Null(dialogs.AskForText("Save preset", "Name", "My style"));
        Assert.Null(dialogs.EditSettings(new AppSettings()));
    }
}
```

- [ ] **Step 3: Run them to watch them fail**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj --filter DialogServiceTests`
Expected: FAIL — `AvaloniaDialogService` does not exist.

- [ ] **Step 4: Build the message window**

`src/TrispotQR.UI/Views/MessageWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="TrispotQR.UI.Views.MessageWindow"
        SizeToContent="Height"
        Width="440"
        CanResize="False"
        WindowStartupLocation="CenterOwner"
        ShowInTaskbar="False">
  <StackPanel Margin="20" Spacing="16">
    <TextBlock x:Name="MessageText" TextWrapping="Wrap" />
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
      <Button x:Name="CancelButton" Click="OnCancel" IsVisible="False" />
      <Button x:Name="ConfirmButton" Click="OnConfirm" IsDefault="True" />
    </StackPanel>
  </StackPanel>
</Window>
```

`src/TrispotQR.UI/Views/MessageWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace TrispotQR.UI.Views;

/// <summary>
/// One window behind every message and confirmation the app shows.
///
/// A single window rather than one per message type, because the only differences are the
/// text and whether there is a second button. WPF gave this away for free as MessageBox;
/// Avalonia has no equivalent, by design, since there is no such thing on every platform.
/// </summary>
public partial class MessageWindow : Window
{
    private bool _confirmed;

    public MessageWindow() => AvaloniaXamlLoader.Load(this);

    public static async Task<bool> ShowAsync(
        Window owner,
        string title,
        string message,
        string confirmLabel,
        string? cancelLabel,
        bool defaultToConfirm)
    {
        var window = new MessageWindow { Title = title };
        window.MessageText.Text = message;
        window.ConfirmButton.Content = confirmLabel;

        if (cancelLabel is not null)
        {
            window.CancelButton.Content = cancelLabel;
            window.CancelButton.IsVisible = true;
            window.CancelButton.IsDefault = !defaultToConfirm;
            window.ConfirmButton.IsDefault = defaultToConfirm;
        }

        await window.ShowDialog(owner);
        return window._confirmed;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _confirmed = false;
        Close();
    }
}
```

- [ ] **Step 5: Implement the dialog service**

`src/TrispotQR.UI/Services/AvaloniaDialogService.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TrispotQR.Core.Presets;
using TrispotQR.UI.Views;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Services;

/// <summary>
/// Everything the view model needs from the world outside it, in Avalonia terms.
///
/// Three members are not implemented in this phase and return null, which every caller in
/// MainViewModel already reads as "the user cancelled": the logo picker, the save-preset
/// prompt and the settings window all arrive in Phase 2c along with the UI that reaches them.
/// </summary>
public sealed class AvaloniaDialogService(Window owner) : IDialogService
{
    /// <summary>Generous, because the wait is on a person rather than on code.</summary>
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromMinutes(10);

    private readonly Window _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public string? AskForSavePath(string title, string filter, string defaultExtension, string suggestedName, string? directory)
    {
        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = defaultExtension.TrimStart('.'),
            FileTypeChoices = [.. FileFilter.Parse(filter)],
            ShowOverwritePrompt = true,
        };

        if (directory is not null)
        {
            options.SuggestedStartLocation = DispatcherWait.For(
                _owner.StorageProvider.TryGetFolderFromPathAsync(directory), DialogTimeout);
        }

        var file = DispatcherWait.For(_owner.StorageProvider.SaveFilePickerAsync(options), DialogTimeout);

        // TryGetLocalPath returns null for a location with no file system path, such as a
        // cloud provider on Android. On desktop that means the save cannot proceed, and null
        // is what the view model reads as a cancelled dialog.
        return file?.TryGetLocalPath();
    }

    /// <summary>Phase 2c, with the logo picker. Null reads as a cancelled dialog.</summary>
    public string? AskForImage(string? directory) => null;

    /// <summary>Phase 2c, with the save-preset flow. Null reads as a cancelled dialog.</summary>
    public string? AskForText(string title, string prompt, string initialValue) => null;

    /// <summary>Phase 2c, with the settings window. Null reads as a cancelled dialog.</summary>
    public AppSettings? EditSettings(AppSettings current) => null;

    public bool Confirm(string title, string message) =>
        DispatcherWait.For(
            MessageWindow.ShowAsync(_owner, title, message, "OK", "Cancel", defaultToConfirm: true),
            DialogTimeout);

    public bool ConfirmRisk(string heading, string message, string proceedLabel, bool defaultToProceed, bool severe) =>
        DispatcherWait.For(
            MessageWindow.ShowAsync(_owner, heading, message, proceedLabel, "Cancel", defaultToProceed),
            DialogTimeout);

    public void ShowError(string title, string message) =>
        DispatcherWait.For(
            MessageWindow.ShowAsync(_owner, title, message, "OK", cancelLabel: null, defaultToConfirm: true),
            DialogTimeout);

    public void ShowInformation(string title, string message) =>
        DispatcherWait.For(
            MessageWindow.ShowAsync(_owner, title, message, "OK", cancelLabel: null, defaultToConfirm: true),
            DialogTimeout);
}
```

- [ ] **Step 6: Run the tests and watch them pass**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`
Expected: PASS, 19 tests.

- [ ] **Step 7: Commit**

```bash
git add src/TrispotQR.UI/Services src/TrispotQR.UI/Views tests/TrispotQR.UI.Tests/DialogServiceTests.cs
git commit -m "Add the Avalonia dialog service"
```

---

### Task 6: The main window and the composition root

**Files:**
- Create: `src/TrispotQR.UI/MainWindow.axaml`, `src/TrispotQR.UI/MainWindow.axaml.cs`
- Create: `src/TrispotQR.UI/Converters/UiConverters.cs`
- Modify: `src/TrispotQR.UI/App.axaml.cs`
- Test: `tests/TrispotQR.UI.Tests/MainWindowTests.cs`

**Interfaces:**
- Consumes: `QrPreview` (Task 2), `AvaloniaUiTimer` and `AvaloniaImageClipboard` (Task 4), `AvaloniaDialogService` (Task 5). `TrispotQR.ViewModels.MainViewModel(IDialogService dialogs, IUiTimer timer, IImageClipboard clipboard, PresetStore? presets = null, AppSettingsStore? settingsStore = null)`, and its members `ContentEditors`, `SelectedContent`, `CapacityText`, `PreviewDrawing`, `StatusText`, `StatusDetail`, `Verdict`, `SizeSummary`, `CanExport`, `ExportBlockedReason`, `SavePngCommand`, `SaveSvgCommand`, `CopyCommand`, `RefreshNow()`, `SaveSession(double, double)`. `TrispotQR.ViewModels.{ContentEditor, PlainTextEditor, LinkEditor}`. `TrispotQR.Core.Validation.ScanVerdict` with members `Good`, `Risky` and `Bad`.
- Produces: `TrispotQR.UI.MainWindow`, and `TrispotQR.UI.Converters.VerdictToBrushConverter`.

The window is the composition root: it constructs the four services and the view model, because `AvaloniaDialogService` and `AvaloniaImageClipboard` both need the window itself.

- [ ] **Step 1: Write the failing window tests**

`tests/TrispotQR.UI.Tests/MainWindowTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TrispotQR.UI;
using TrispotQR.UI.Controls;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Tests;

public class MainWindowTests
{
    /// <summary>
    /// Opens the window and selects the plain-text editor.
    ///
    /// Selecting it explicitly matters: MainViewModel restores the last-used content type from
    /// the real settings file, and MainWindow is the composition root so no test can hand it a
    /// different store. Assuming plain text is selected would make these tests pass or fail
    /// depending on what the developer last did in the shipping app.
    /// </summary>
    private static (MainWindow Window, MainViewModel Model, PlainTextEditor Editor) Open()
    {
        var window = new MainWindow();
        window.Show();

        var model = Assert.IsType<MainViewModel>(window.DataContext);
        var editor = model.ContentEditors.OfType<PlainTextEditor>().Single();
        model.SelectedContent = editor;

        return (window, model, editor);
    }

    [AvaloniaFact]
    public void OpensWithAViewModelAttached()
    {
        var (_, model, editor) = Open();

        Assert.NotEmpty(model.ContentEditors);
        Assert.Same(editor, model.SelectedContent);
    }

    [AvaloniaFact]
    public void ShowsAPreviewOnceThereIsContent()
    {
        var (window, model, editor) = Open();

        editor.Text = "https://www.example.org";

        // RefreshNow skips the debounce timer, which is what the window uses in normal running.
        // Waiting on a real 150ms tick here would make the test slow and flaky for no gain.
        model.RefreshNow();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(model.PreviewDrawing);

        var preview = window.GetVisualDescendants().OfType<QrPreview>().Single();
        Assert.NotNull(preview.Drawing);
    }

    [AvaloniaFact]
    public void CannotExportWithNoContent()
    {
        var (_, model, editor) = Open();

        editor.Text = string.Empty;

        Assert.False(model.CanExport);
        Assert.False(model.SavePngCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void CanExportOnceThereIsContent()
    {
        var (_, model, editor) = Open();

        editor.Text = "https://www.example.org";
        model.RefreshNow();
        Dispatcher.UIThread.RunJobs();

        Assert.True(model.CanExport);
        Assert.True(model.SavePngCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void TheWindowRendersWithoutBindingFailures()
    {
        // Compiled bindings turn a mistyped binding into a build error, but a binding to a
        // missing DataTemplate still only shows up at runtime. Capturing a frame forces the
        // whole visual tree to render, which is what surfaces that.
        var (window, model, editor) = Open();

        editor.Text = "test";
        model.RefreshNow();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.CaptureRenderedFrame());
    }
}
```

Add `using Avalonia.VisualTree;` for `GetVisualDescendants`.

- [ ] **Step 2: Run them to watch them fail**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj --filter MainWindowTests`
Expected: FAIL — `MainWindow` does not exist.

- [ ] **Step 3: Write the verdict converter**

`src/TrispotQR.UI/Converters/UiConverters.cs`:

```csharp
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using TrispotQR.Core.Validation;

namespace TrispotQR.UI.Converters;

/// <summary>
/// Colours the scannability badge. Green means it read back cleanly, amber means it read back
/// but carries something cameras often trip on, red means it did not read at all.
/// </summary>
public sealed class VerdictToBrushConverter : IValueConverter
{
    public static readonly VerdictToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            ScanVerdict.Good => new SolidColorBrush(Color.FromRgb(0x1B, 0x7F, 0x37)),
            ScanVerdict.Risky => new SolidColorBrush(Color.FromRgb(0xB5, 0x6E, 0x00)),
            _ => new SolidColorBrush(Color.FromRgb(0xB3, 0x26, 0x1E)),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A verdict cannot be recovered from a colour.");
}
```

- [ ] **Step 4: Write the window**

`src/TrispotQR.UI/MainWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:TrispotQR.UI.Controls"
        xmlns:converters="clr-namespace:TrispotQR.UI.Converters"
        xmlns:vm="clr-namespace:TrispotQR.ViewModels;assembly=TrispotQR.ViewModels"
        x:Class="TrispotQR.UI.MainWindow"
        x:DataType="vm:MainViewModel"
        Title="TrispotQR"
        Width="1000" Height="700"
        MinWidth="760" MinHeight="560">

  <Window.Resources>
    <converters:VerdictToBrushConverter x:Key="VerdictToBrush" />
  </Window.Resources>

  <Window.DataTemplates>
    <DataTemplate DataType="vm:PlainTextEditor">
      <TextBox Text="{Binding Text}" AcceptsReturn="True" TextWrapping="Wrap" MinHeight="120" />
    </DataTemplate>

    <DataTemplate DataType="vm:LinkEditor">
      <TextBox Text="{Binding Address}" Watermark="www.example.com" />
    </DataTemplate>

    <!-- The remaining five content types keep their WPF fields until Phase 2c. Saying so is
         better than showing an empty panel that looks broken. -->
    <DataTemplate DataType="vm:ContentEditor">
      <TextBlock TextWrapping="Wrap" Opacity="0.7"
                 Text="The fields for this content type arrive in the next phase. Plain text and Link work today." />
    </DataTemplate>
  </Window.DataTemplates>

  <Grid ColumnDefinitions="*,380" Margin="16">

    <!-- Preview -->
    <Grid Grid.Column="0" RowDefinitions="*,Auto,Auto" Margin="0,0,16,0">
      <Border Grid.Row="0" Background="#F4F4F4" CornerRadius="6" Padding="16">
        <controls:QrPreview Drawing="{Binding PreviewDrawing}" />
      </Border>

      <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="8" Margin="0,12,0,0">
        <Ellipse Width="10" Height="10" VerticalAlignment="Center"
                 Fill="{Binding Verdict, Converter={StaticResource VerdictToBrush}}" />
        <TextBlock Text="{Binding StatusText}" FontWeight="SemiBold" VerticalAlignment="Center" />
      </StackPanel>

      <TextBlock Grid.Row="2" Text="{Binding StatusDetail}" TextWrapping="Wrap"
                 Opacity="0.75" Margin="18,4,0,0" />
    </Grid>

    <!-- Controls -->
    <ScrollViewer Grid.Column="1">
      <StackPanel Spacing="16">

        <StackPanel Spacing="6">
          <TextBlock Text="What goes in the code" FontWeight="SemiBold" />
          <ComboBox ItemsSource="{Binding ContentEditors}"
                    SelectedItem="{Binding SelectedContent}"
                    HorizontalAlignment="Stretch">
            <ComboBox.ItemTemplate>
              <DataTemplate DataType="vm:ContentEditor">
                <TextBlock Text="{Binding Title}" />
              </DataTemplate>
            </ComboBox.ItemTemplate>
          </ComboBox>
          <TextBlock Text="{Binding SelectedContent.Hint}" TextWrapping="Wrap" Opacity="0.7" FontSize="12" />
        </StackPanel>

        <ContentControl Content="{Binding SelectedContent}" />

        <TextBlock Text="{Binding CapacityText}" Opacity="0.7" FontSize="12" />

        <StackPanel Spacing="6">
          <TextBlock Text="Save" FontWeight="SemiBold" />
          <TextBlock Text="{Binding SizeSummary}" Opacity="0.7" FontSize="12" />
          <TextBlock Text="{Binding ExportBlockedReason}" TextWrapping="Wrap" FontSize="12"
                     Foreground="#B3261E" IsVisible="{Binding !CanExport}" />
          <StackPanel Orientation="Horizontal" Spacing="8">
            <Button Content="Save PNG" Command="{Binding SavePngCommand}" />
            <Button Content="Save SVG" Command="{Binding SaveSvgCommand}" />
            <Button Content="Copy" Command="{Binding CopyCommand}" />
          </StackPanel>
        </StackPanel>

        <TextBlock TextWrapping="Wrap" Opacity="0.6" FontSize="12"
                   Text="Styling, presets, the logo picker and settings are still in the Windows build. They arrive here next." />

      </StackPanel>
    </ScrollViewer>
  </Grid>
</Window>
```

`src/TrispotQR.UI/MainWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TrispotQR.UI.Services;
using TrispotQR.ViewModels;

namespace TrispotQR.UI;

/// <summary>
/// The shell, and the composition root.
///
/// The view model is built here rather than in App because two of its services need a window:
/// the dialog service owns the parent for every modal, and the clipboard reaches the system
/// through a TopLevel. Constructing them anywhere else would mean handing the window in later
/// and leaving the view model half-built until then.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _model;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        _model = new MainViewModel(
            new AvaloniaDialogService(this),
            new AvaloniaUiTimer(),
            new AvaloniaImageClipboard(this));

        DataContext = _model;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _model.SaveSession(Width, Height);
        base.OnClosing(e);
    }
}
```

- [ ] **Step 5: Point App at the window**

Replace `src/TrispotQR.UI/App.axaml.cs` with:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace TrispotQR.UI;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Only when there is a desktop lifetime. The headless test host has none, and creating
        // a main window there would open one window per test run.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

- [ ] **Step 6: Run the tests and watch them pass**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`
Expected: PASS, 24 tests.

`MainViewModel`'s constructor defaults `PresetStore` and `AppSettingsStore` to real ones, so these tests read and write the same settings folder the app uses. That is acceptable here because none of them writes a preset or a setting. If a later test needs to, it must pass its own store rather than sharing the user's.

- [ ] **Step 7: Run the whole solution and commit**

Run: `dotnet test TrispotQR.slnx`
Expected: PASS — 415 + 59 + 220 + 24 = 718.

```bash
git add src/TrispotQR.UI tests/TrispotQR.UI.Tests/MainWindowTests.cs
git commit -m "Add the Avalonia main window and wire the view model"
```

---

### Task 7: The desktop entry point

**Files:**
- Create: `src/TrispotQR.Desktop/TrispotQR.Desktop.csproj`
- Create: `src/TrispotQR.Desktop/Program.cs`
- Modify: `TrispotQR.slnx`
- Modify: `README.md`

**Interfaces:**
- Consumes: `TrispotQR.UI.App` from Task 1.
- Produces: a runnable `TrispotQR.Desktop` executable.

- [ ] **Step 1: Create the entry point**

`src/TrispotQR.Desktop/TrispotQR.Desktop.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>TrispotQR</AssemblyName>
    <!-- WinExe suppresses the console window on Windows and means nothing on Linux and macOS,
         where it is simply ignored. -->
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia.Desktop" Version="12.1.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\TrispotQR.UI\TrispotQR.UI.csproj" />
  </ItemGroup>

</Project>
```

`src/TrispotQR.Desktop/Program.cs`:

```csharp
using Avalonia;
using TrispotQR.UI;

namespace TrispotQR.Desktop;

internal static class Program
{
    // Avalonia needs this on the main thread, before anything touches the toolkit.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Named exactly this because Avalonia's XAML previewer looks it up by convention.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
```

- [ ] **Step 2: Add it to the solution and build**

```bash
dotnet sln TrispotQR.slnx add src/TrispotQR.Desktop/TrispotQR.Desktop.csproj
dotnet build TrispotQR.slnx
```

Expected: builds with no warnings. If `WithInterFont` cannot be resolved, add `<PackageReference Include="Avalonia.Fonts.Inter" Version="12.1.2" />` to the Desktop project — it ships a bundled font so the app looks the same on a Linux box with no fonts installed.

- [ ] **Step 3: Run it and check the spine by hand**

```bash
dotnet run --project src/TrispotQR.Desktop/TrispotQR.Desktop.csproj
```

Walk through all of this and report what happened at each point:

1. The window opens, roughly 1000x700, with an empty preview area and the save buttons disabled.
2. Choose "Plain text" and type `https://www.example.org`. The preview appears within about a second and the badge goes green.
3. Resize the window larger. The code stays sharp rather than going blocky — that is the vector preview doing its job.
4. Click Save PNG. A real save dialog opens, defaulting to a `.png` name. Save it, then open the file and confirm it is the same code.
5. Click Save SVG, save it, open it in a browser, and confirm it matches.
6. Click Copy, then paste into any editor that accepts an image.
7. Switch the content type to Wi-Fi. The placeholder text appears rather than an empty panel.
8. Switch back to Plain text. The typed text is still there.
9. Close the window. It closes without an error.

Anything that fails here is a finding to report, not something to patch over.

- [ ] **Step 4: Note the new app in the README**

Add to `README.md`, under the existing download section, without changing the download links — the Windows WPF build is still the one people should use:

```markdown
### Mac and Linux

A cross-platform build is in progress. The Windows download above is the one to use today;
Mac and Linux builds arrive once the port reaches feature parity.
```

- [ ] **Step 5: Run everything, commit and push**

Run: `dotnet test TrispotQR.slnx`
Expected: PASS, 718 tests.

```bash
git add src/TrispotQR.Desktop TrispotQR.slnx README.md
git commit -m "Add the Avalonia desktop entry point"
git push
```

Report the CI conclusions for all four jobs.

---

## Done when

- `dotnet run --project src/TrispotQR.Desktop` opens a window that encodes, previews, and saves a PNG and an SVG.
- 718 tests pass, of which 498 run on Windows, Linux and macOS.
- CI is green on all four jobs, with the Avalonia UI tests running in the portable matrix.
- The WPF app still builds and its 220 tests still pass.
- `MainViewModel` is unchanged.
