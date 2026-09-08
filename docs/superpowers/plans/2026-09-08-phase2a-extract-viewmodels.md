# Phase 2a: extract the view models into a platform-neutral project

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move roughly 1,660 lines of view model out of the WPF app into a new `net10.0` project with no UI framework, so a future Avalonia app can share them and so they come under the existing three-OS CI.

**Architecture:** The view models are already close to neutral. `ContentEditors` and `ObservableObject` have no UI dependency at all. `MainViewModel` has exactly three: a WPF `Color` on five properties, a `DispatcherTimer` for the render debounce, and an `ImageSource` for the preview. Each is replaced in its own task, smallest first, so the tree compiles and the suite passes at every step. The WPF app keeps working throughout and only ever gains thin adapters.

**Tech Stack:** .NET 10, xUnit. No new packages.

**Spec:** `docs/superpowers/specs/2026-09-04-cross-platform-port-design.md`

## Global Constraints

- **Behaviour is preserved.** No user-visible change. This is a move plus three seam replacements.
- **Every task leaves a compiling tree and a green suite.** Never commit red. The suite is 683 today: 415 in `TrispotQR.Core.Tests`, 268 in `TrispotQR.Tests`.
- **`TrispotQR.ViewModels` must never reference WPF or Avalonia.** No `System.Windows`, no `UseWPF`, no `net10.0-windows`. Task 6 adds a test that fails if that stops being true.
- **`TrispotQR.ViewModels` references `TrispotQR.Core` only.** It must not reference `TrispotQR.App`.
- Commit messages carry **no `Co-Authored-By` trailer and no mention of Claude or AI**. Every commit in this repository follows that.
- Line endings LF, enforced by `.gitattributes`. Comments explain why, not what.
- Use `git mv` when moving files so history follows them.

## Facts verified against the tree before this plan was written

- `System.Windows.Input.ICommand` is available in plain `net10.0` with no WPF. Compiled and confirmed. `RelayCommand` moves unchanged.
- `TaskScheduler` in `MainViewModel._uiScheduler` is `System.Threading.Tasks`, not WPF. It stays as it is.
- `ContentEditors.cs` (529 lines) and `ObservableObject.cs` (75 lines) contain no UI type at all.
- `MainViewModel.cs` is 1,055 lines. Its only UI couplings are `Color` on five properties, `DispatcherTimer` (lines 34, 40, 96-99, 550-551, 560) and `ImageSource` (lines 53, 388, 577, 590, 600, 852, 1040, 1048).
- `IDialogService` and its `DialogService` implementation share one file, `src/TrispotQR.App/Services/DialogService.cs`.
- `PresetItem` is declared at the bottom of `MainViewModel.cs`.

---

## File Structure

**New:**

| File | Responsibility |
|---|---|
| `src/TrispotQR.ViewModels/TrispotQR.ViewModels.csproj` | `net10.0`, references Core only |
| `src/TrispotQR.ViewModels/IUiTimer.cs` | The one scheduling primitive the view model needs |
| `src/TrispotQR.ViewModels/IDialogService.cs` | The interface, moved out of the WPF file |
| `tests/TrispotQR.ViewModels.Tests/` | Cross-platform view model tests, joins the CI matrix |
| `src/TrispotQR.App/Services/WpfUiTimer.cs` | `DispatcherTimer` behind `IUiTimer` |
| `src/TrispotQR.App/Converters/QrDrawingToImageSourceConverter.cs` | Draws a `QrDrawing` for WPF binding |
| `src/TrispotQR.App/Converters/RgbColorToColorConverter.cs` | Bridges `RgbColor` to WPF `Color` in XAML |

**Moved into `TrispotQR.ViewModels`:** `ObservableObject.cs`, `ContentEditors.cs`, `MainViewModel.cs`, and the `IDialogService` interface.

**Stays in `TrispotQR.App`:** `DialogService` (the implementation), `ThemeManager`, `WpfImageClipboard`, `WpfQrRenderer`, `WpfGeometryAdapter`, all views and XAML.

---

## Task 1: The neutral project, with the files that already have no UI dependency

**Files:**
- Create: `src/TrispotQR.ViewModels/TrispotQR.ViewModels.csproj`
- Move: `src/TrispotQR.App/ViewModels/ObservableObject.cs` and `ContentEditors.cs` into `src/TrispotQR.ViewModels/`
- Modify: `src/TrispotQR.App/TrispotQR.App.csproj`, `TrispotQR.slnx`, and every file naming `TrispotQR.App.ViewModels`

**Interfaces:**
- Consumes: nothing.
- Produces: namespace `TrispotQR.ViewModels` holding `ObservableObject`, `RelayCommand`, `RelayCommand<T>`, `ContentEditor` and its seven subclasses.

- [ ] **Step 1: Create the project**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!--
      Plain net10.0 with no UI framework, deliberately. These view models are shared by the
      WPF app today and an Avalonia app later, and CI runs them on Linux and macOS. A
      reference to either toolkit here would end all three of those at once.

      Anything needing a window, a dispatcher or a drawing type belongs in the app.
    -->
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>TrispotQR.ViewModels</RootNamespace>
    <Description>View models and presentation logic, shared across UI toolkits.</Description>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\TrispotQR.Core\TrispotQR.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="TrispotQR.ViewModels.Tests" />
    <!-- The app's assembly is named TrispotQR, not TrispotQR.App. See its AssemblyName. -->
    <InternalsVisibleTo Include="TrispotQR" />
    <InternalsVisibleTo Include="TrispotQR.Tests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Move the two files and change their namespace**

```bash
git mv src/TrispotQR.App/ViewModels/ObservableObject.cs src/TrispotQR.ViewModels/ObservableObject.cs
git mv src/TrispotQR.App/ViewModels/ContentEditors.cs  src/TrispotQR.ViewModels/ContentEditors.cs
```

In both files change `namespace TrispotQR.App.ViewModels;` to `namespace TrispotQR.ViewModels;`.

- [ ] **Step 3: Wire the references**

Add to `src/TrispotQR.App/TrispotQR.App.csproj`, in the existing `ProjectReference` group:

```xml
    <ProjectReference Include="..\TrispotQR.ViewModels\TrispotQR.ViewModels.csproj" />
```

Add to `TrispotQR.slnx`, inside the `/src/` folder:

```xml
    <Project Path="src/TrispotQR.ViewModels/TrispotQR.ViewModels.csproj" />
```

- [ ] **Step 4: Fix every consumer**

`MainViewModel.cs` still declares `namespace TrispotQR.App.ViewModels;` and now needs `using TrispotQR.ViewModels;`. Every other file that relied on `TrispotQR.App.ViewModels` for `ContentEditor`, `ObservableObject` or `RelayCommand` needs the same. Find them with:

```bash
grep -rln "TrispotQR.App.ViewModels" src tests --include=*.cs --include=*.xaml
```

`MainWindow.xaml` maps the namespace with `xmlns:vm="clr-namespace:TrispotQR.App.ViewModels"`. The content editor `DataTemplate`s reference `vm:PlainTextEditor` and friends, which now live in another assembly, so add a second mapping:

```xml
xmlns:vm="clr-namespace:TrispotQR.App.ViewModels"
xmlns:ed="clr-namespace:TrispotQR.ViewModels;assembly=TrispotQR.ViewModels"
```

and change each `DataType="{x:Type vm:PlainTextEditor}"` to `DataType="{x:Type ed:PlainTextEditor}"`. There are seven.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test TrispotQR.slnx`
Expected: PASS, 683. A missing XAML namespace surfaces as a runtime binding failure rather than a compile error, so watch `MainWindowSmokeTests`: it fails the build on binding errors and is the test that catches this.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Move the view models that were already framework-neutral

ContentEditors and ObservableObject contain no UI type at all and were only
in the WPF project by accident of history. A future Avalonia app needs them
and so does cross-platform CI."
```

---

## Task 2: Move the IDialogService interface

**Files:**
- Create: `src/TrispotQR.ViewModels/IDialogService.cs`
- Modify: `src/TrispotQR.App/Services/DialogService.cs`, plus every test stub implementing it

**Interfaces:**
- Consumes: Task 1's project.
- Produces: `TrispotQR.ViewModels.IDialogService`, with its seven members unchanged.

- [ ] **Step 1: Move the interface, verbatim**

Cut the `IDialogService` interface out of `src/TrispotQR.App/Services/DialogService.cs` into a new `src/TrispotQR.ViewModels/IDialogService.cs`, changing only the namespace and the summary. Keep every member and every doc comment, including the one on `ConfirmRisk` explaining what `severe` means.

```csharp
namespace TrispotQR.ViewModels;

/// <summary>
/// Everything the view model needs from the world outside it. Kept behind an interface so
/// the view model can be exercised without a window on screen, and so the same view model
/// serves a WPF window today and an Avalonia one later.
/// </summary>
public interface IDialogService
{
    string? AskForSavePath(string title, string filter, string defaultExtension, string suggestedName, string? directory);

    string? AskForImage(string? directory);

    string? AskForText(string title, string prompt, string initialValue);

    bool Confirm(string title, string message);

    /// <summary>
    /// Asks whether to go ahead with something risky, with the action named on the button.
    /// Returns true to proceed.
    /// </summary>
    /// <param name="severe">True when the code did not scan at all, false when it merely might not.</param>
    bool ConfirmRisk(string heading, string message, string proceedLabel, bool defaultToProceed, bool severe);

    void ShowError(string title, string message);

    void ShowInformation(string title, string message);
}
```

`DialogService.cs` keeps only the implementation and gains `using TrispotQR.ViewModels;`.

- [ ] **Step 2: Run the whole suite**

Run: `dotnet test TrispotQR.slnx`
Expected: PASS, 683. Several test files declare their own stub implementing this interface; each needs the new `using`. Find them with `grep -rln "IDialogService" tests`.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "Move IDialogService to the shared project, leaving the WPF implementation behind"
```

---

## Task 3: Replace the DispatcherTimer with an IUiTimer

**Files:**
- Create: `src/TrispotQR.ViewModels/IUiTimer.cs`, `src/TrispotQR.App/Services/WpfUiTimer.cs`
- Modify: `src/TrispotQR.App/ViewModels/MainViewModel.cs`, and wherever the app constructs the view model
- Test: `tests/TrispotQR.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: Task 1's project.
- Produces: `TrispotQR.ViewModels.IUiTimer` with `TimeSpan Interval { get; set; }`, `event EventHandler Tick`, `void Start()`, `void Stop()`. `TrispotQR.App.Services.WpfUiTimer : IUiTimer`. `MainViewModel`'s constructor gains a trailing `IUiTimer? timer = null`.

- [ ] **Step 1: Write the failing test**

Add to `tests/TrispotQR.Tests/MainViewModelTests.cs`:

```csharp
    /// <summary>
    /// The render debounce is the only scheduling the view model owns, and it was a WPF
    /// DispatcherTimer. Behind an interface it can be driven directly, which is what a
    /// non-WPF toolkit needs and what lets this test prove the debounce without waiting on
    /// a real clock.
    /// </summary>
    [Fact]
    public void TheDebounce_RendersOnceTheTimerFires()
    {
        var timer = new FakeUiTimer();
        var vm = Create(timer);

        ((PlainTextEditor)vm.ContentEditors[0]).Text = "https://example.org";

        Assert.True(timer.IsRunning, "typing should have started the debounce");

        timer.Fire();

        Assert.False(timer.IsRunning, "the timer should stop itself when it fires");
        Assert.True(vm.CanExport, "the render should have happened");
    }

    private sealed class FakeUiTimer : IUiTimer
    {
        public TimeSpan Interval { get; set; }

        public bool IsRunning { get; private set; }

        public event EventHandler? Tick;

        public void Start() => IsRunning = true;

        public void Stop() => IsRunning = false;

        /// <summary>Stands in for the clock.</summary>
        public void Fire() => Tick?.Invoke(this, EventArgs.Empty);
    }
```

`Create` is the existing helper in that file. Give it an optional `IUiTimer? timer = null` parameter that it passes through to the view model.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~TheDebounce_RendersOnceTheTimerFires"`
Expected: FAIL to compile, `IUiTimer` does not exist.

- [ ] **Step 3: Write the interface**

`src/TrispotQR.ViewModels/IUiTimer.cs`:

```csharp
namespace TrispotQR.ViewModels;

/// <summary>
/// A repeating timer whose tick arrives where the UI can act on it.
///
/// This exists because the view model debounces rendering, and every UI toolkit has its own
/// dispatcher timer. One small interface keeps the view model free of all of them, and lets
/// a test drive the debounce directly rather than waiting on a real clock.
/// </summary>
public interface IUiTimer
{
    TimeSpan Interval { get; set; }

    event EventHandler Tick;

    void Start();

    void Stop();
}
```

- [ ] **Step 4: Write the WPF implementation**

`src/TrispotQR.App/Services/WpfUiTimer.cs`:

```csharp
using System.Windows.Threading;
using TrispotQR.ViewModels;

namespace TrispotQR.App.Services;

/// <summary>WPF's dispatcher timer behind the shared interface.</summary>
public sealed class WpfUiTimer : IUiTimer
{
    private readonly DispatcherTimer _timer = new();

    public WpfUiTimer() => _timer.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);

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

- [ ] **Step 5: Switch the view model over**

In `MainViewModel.cs`, delete `using System.Windows.Threading;`, change the field to `private readonly IUiTimer _debounce;`, add a trailing constructor parameter `IUiTimer? timer = null`, and replace the construction:

```csharp
        _debounce = timer ?? new WpfUiTimer();
        _debounce.Interval = RenderDebounce;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Render();
        };
```

**That default is temporary.** `WpfUiTimer` lives in the app, which the view model cannot reference once it moves in Task 6. Task 6 removes the default and makes the parameter required, with the app supplying it. Leaving it here keeps this task's diff small and every existing caller working.

`ScheduleRender` and `RefreshNow` need no change: `Stop()` and `Start()` are both on the interface.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test TrispotQR.slnx`
Expected: PASS, 684.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Put the render debounce behind an interface instead of a DispatcherTimer

Every UI toolkit has its own dispatcher timer. One small interface keeps the
view model free of all of them, and lets a test drive the debounce directly
rather than waiting on a real clock."
```

---

## Task 4: The preview becomes a QrDrawing, not an ImageSource

The view model should not hold a rendered WPF image. It should say what to draw and let the view decide how. This is the one change in the phase that is a design improvement rather than a move.

**Files:**
- Create: `src/TrispotQR.App/Converters/QrDrawingToImageSourceConverter.cs`
- Modify: `src/TrispotQR.App/ViewModels/MainViewModel.cs`, `src/TrispotQR.App/MainWindow.xaml`
- Test: `tests/TrispotQR.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `QrDrawing` from `TrispotQR.Core.Rendering`.
- Produces: `MainViewModel.PreviewDrawing` of type `QrDrawing?`; `PresetItem.ThumbnailDrawing` of type `QrDrawing?`; `QrDrawingToImageSourceConverter : IValueConverter`.

- [ ] **Step 1: Write the failing test**

```csharp
    /// <summary>
    /// The view model describes what to draw; turning that into pixels is the view's job.
    /// Holding a WPF image here is what tied the view model to one toolkit.
    /// </summary>
    [Fact]
    public void ThePreview_IsADrawingRatherThanARenderedImage()
    {
        var vm = CreateWithContent("https://example.org");

        Assert.NotNull(vm.PreviewDrawing);
        Assert.True(vm.PreviewDrawing!.SizeInUnits > 0);
        Assert.NotEmpty(vm.PreviewDrawing.Layers);
    }

    [Fact]
    public void AnEmptyBox_ClearsThePreview()
    {
        var vm = Create();

        ((PlainTextEditor)vm.ContentEditors[0]).Text = string.Empty;
        vm.RefreshNow();

        Assert.Null(vm.PreviewDrawing);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~ThePreview_IsADrawingRatherThanARenderedImage"`
Expected: FAIL to compile, `PreviewDrawing` does not exist.

- [ ] **Step 3: Change the view model**

Replace the `ImageSource? _preview` field and the `Preview` property with:

```csharp
    private QrDrawing? _previewDrawing;

    /// <summary>
    /// What the preview should show, described rather than rendered. The view turns it into
    /// pixels, which is what lets the same view model serve WPF and Avalonia.
    /// </summary>
    public QrDrawing? PreviewDrawing
    {
        get => _previewDrawing;
        private set => SetField(ref _previewDrawing, value);
    }
```

At the three assignment sites, `Preview = null` becomes `PreviewDrawing = null`, and `Preview = WpfQrRenderer.RenderToDrawingImage(_drawing)` becomes `PreviewDrawing = _drawing`.

`BuildThumbnail` returns `QrDrawing?` and stops calling the renderer:

```csharp
    private static QrDrawing? BuildThumbnail(QrStyle style)
    {
        var encoded = QrEncoder.Encode(ThumbnailPayload, style.Ecc);

        return encoded.Success
            ? QrGeometryBuilder.Build(encoded.Matrix!, style with { QuietZoneModules = 2 })
            : null;
    }
```

Use whatever the file already calls the thumbnail payload constant; do not invent a new one.

`PresetItem`'s constructor parameter and its `Thumbnail` property become `QrDrawing?`, renamed `ThumbnailDrawing`. Delete `using System.Windows.Media;` from `MainViewModel.cs` if nothing else in the file needs it.

- [ ] **Step 4: Add the converter**

`src/TrispotQR.App/Converters/QrDrawingToImageSourceConverter.cs`:

```csharp
using System.Globalization;
using System.Windows.Data;
using TrispotQR.App.Rendering;
using TrispotQR.Core.Rendering;

namespace TrispotQR.App.Converters;

/// <summary>
/// Renders a <see cref="QrDrawing"/> for WPF to display.
///
/// The view model deliberately hands out the drawing rather than an image, so this is where
/// the toolkit-specific step happens. Phase 2b adds an Avalonia equivalent beside it and
/// nothing in the view model changes.
/// </summary>
public sealed class QrDrawingToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is QrDrawing drawing ? WpfQrRenderer.RenderToDrawingImage(drawing) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A drawing cannot be recovered from a rendered image.");
}
```

- [ ] **Step 5: Update the XAML**

In `MainWindow.xaml`'s resources add:

```xml
        <conv:QrDrawingToImageSourceConverter x:Key="DrawingToImage" />
```

The preview image binds through it, and so does the `DataTrigger` that shows the placeholder:

```xml
                    <Image x:Name="PreviewImage"
                           Source="{Binding PreviewDrawing, Converter={StaticResource DrawingToImage}}"
```

```xml
                                    <DataTrigger Binding="{Binding PreviewDrawing}" Value="{x:Null}">
```

The preset thumbnails bind the same way. Find them with `grep -n "Thumbnail" src/TrispotQR.App/MainWindow.xaml`.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test TrispotQR.slnx`
Expected: PASS, 686. `MainWindowSmokeTests` asserts the preview image has a source, so a broken converter binding fails there rather than silently showing nothing.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Let the view model describe the preview instead of rendering it

Holding a WPF ImageSource is what tied the view model to one toolkit. It now
exposes the QrDrawing and the view renders it, which is both the smaller
dependency and the more honest split of responsibility."
```

---

## Task 5: The colour properties become RgbColor

**Files:**
- Create: `src/TrispotQR.App/Converters/RgbColorToColorConverter.cs`
- Modify: `src/TrispotQR.App/ViewModels/MainViewModel.cs`, `src/TrispotQR.App/MainWindow.xaml`
- Test: `tests/TrispotQR.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `RgbColor` from `TrispotQR.Core.Primitives`.
- Produces: `MainViewModel.Foreground`, `.CustomBackground`, `.MarkerFrameColor`, `.MarkerCenterColor` and `.OutlineColor`, all typed `RgbColor`; `RgbColorToColorConverter : IValueConverter`.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void TheColourProperties_SpeakCoresColourType()
    {
        var vm = Create();

        vm.Foreground = RgbColor.FromRgb(0x1B, 0x2A, 0x4A);

        Assert.Equal(RgbColor.FromRgb(0x1B, 0x2A, 0x4A), vm.Foreground);
    }
```

If the file already has a way to observe the resulting `QrStyle`, assert on that too. Do not add a property to the view model purely to make this test possible.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TrispotQR.slnx --filter "FullyQualifiedName~TheColourProperties_SpeakCoresColourType"`
Expected: FAIL to compile, cannot convert `RgbColor` to `Color`.

- [ ] **Step 3: Change the five properties**

Each currently crosses through `WpfGeometryAdapter`. They become direct:

```csharp
    public RgbColor Foreground
    {
        get => _style.Foreground;
        set => UpdateStyle(s => s with { Foreground = value });
    }
```

Apply the same shape to `CustomBackground`, `MarkerFrameColor` and `MarkerCenterColor`, keeping each one's existing null handling if it has any. `OutlineColor` keeps its nested update:

```csharp
    public RgbColor OutlineColor
    {
        get => _style.Outline.Color;
        set => UpdateStyle(s => s with { Outline = s.Outline with { Color = value } });
    }
```

Delete `using System.Windows.Media;` and the now-unused `WpfGeometryAdapter` calls from `MainViewModel.cs`.

- [ ] **Step 4: Add the converter**

`src/TrispotQR.App/Converters/RgbColorToColorConverter.cs`:

```csharp
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TrispotQR.App.Rendering;
using TrispotQR.Core.Primitives;

namespace TrispotQR.App.Converters;

/// <summary>
/// Bridges Core's colour type to WPF's for XAML binding.
///
/// The colour picker is a WPF control and speaks WPF colours; the view model speaks Core's.
/// This is the one place they meet, so an Avalonia app replaces this file and nothing else.
/// </summary>
public sealed class RgbColorToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RgbColor colour ? WpfGeometryAdapter.ToColor(colour) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Color colour ? WpfGeometryAdapter.ToRgbColor(colour) : null;
}
```

- [ ] **Step 5: Update the XAML**

Add `<conv:RgbColorToColorConverter x:Key="RgbToColor" />` to the resources, then put every colour binding through it, keeping two-way wherever it already was. Find them with:

```bash
grep -n "Foreground\|CustomBackground\|MarkerFrameColor\|MarkerCenterColor\|OutlineColor" src/TrispotQR.App/MainWindow.xaml
```

A binding to a colour picker's `SelectedColor` becomes, for example:

```xml
SelectedColor="{Binding Foreground, Converter={StaticResource RgbToColor}, Mode=TwoWay}"
```

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test TrispotQR.slnx`
Expected: PASS, 687. `ColorPickerTests` and `MainWindowSmokeTests` both exercise these bindings; a dropped `Mode=TwoWay` shows up as a colour that will not change.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Speak Core's colour type in the view model, converting only at the XAML boundary"
```

---

## Task 6: Move MainViewModel and prove the project is neutral

**Files:**
- Move: `src/TrispotQR.App/ViewModels/MainViewModel.cs` into `src/TrispotQR.ViewModels/`
- Create: `tests/TrispotQR.ViewModels.Tests/TrispotQR.ViewModels.Tests.csproj`, `tests/TrispotQR.ViewModels.Tests/ViewModelsPortabilityTests.cs`
- Modify: `TrispotQR.slnx`, and wherever the app constructs `MainViewModel`

**Interfaces:**
- Consumes: everything above.
- Produces: `TrispotQR.ViewModels.MainViewModel` and `TrispotQR.ViewModels.PresetItem`; the `IUiTimer` parameter becomes required.

- [ ] **Step 1: Write the failing test**

`tests/TrispotQR.ViewModels.Tests/ViewModelsPortabilityTests.cs`:

```csharp
using System.Reflection;

namespace TrispotQR.ViewModels.Tests;

/// <summary>
/// The shared view models must not depend on any UI toolkit, or the Avalonia app cannot use
/// them and CI cannot run them on Linux and macOS.
///
/// A test rather than a note, because this is the kind of constraint that decays: one
/// convenient using directive for a Color or a Dispatcher and it is gone, on Windows, where
/// nobody would notice. The same guard exists for Core in CorePortabilityTests.
/// </summary>
public class ViewModelsPortabilityTests
{
    private static readonly Assembly ViewModels = typeof(MainViewModel).Assembly;

    [Theory]
    [InlineData("PresentationCore")]
    [InlineData("PresentationFramework")]
    [InlineData("WindowsBase")]
    [InlineData("Avalonia.Base")]
    [InlineData("Avalonia.Controls")]
    public void ViewModels_DoNotReference(string assemblyName)
    {
        var referenced = ViewModels.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.False(
            referenced.Contains(assemblyName, StringComparer.OrdinalIgnoreCase),
            $"TrispotQR.ViewModels references {assemblyName}. Referenced: {string.Join(", ", referenced)}");
    }

    /// <summary>
    /// TargetFrameworkAttribute is NOT the thing to check: its FrameworkName is identical
    /// for net10.0 and net10.0-windows. Since .NET 5 the platform suffix lives here.
    /// </summary>
    [Fact]
    public void ViewModels_TargetAPlatformNeutralFramework() =>
        Assert.Null(ViewModels.GetCustomAttribute<System.Runtime.Versioning.TargetPlatformAttribute>());

    [Fact]
    public void ViewModels_DoNotReferenceTheApp()
    {
        var referenced = ViewModels.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain("TrispotQR", referenced);
    }
}
```

- [ ] **Step 2: Create the test project**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!-- Plain net10.0: these run on Linux and macOS in CI alongside the Core tests. -->
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\TrispotQR.ViewModels\TrispotQR.ViewModels.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

Add it to `TrispotQR.slnx` under the `/tests/` folder.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/TrispotQR.ViewModels.Tests/TrispotQR.ViewModels.Tests.csproj`
Expected: FAIL to compile, `MainViewModel` is not in `TrispotQR.ViewModels` yet.

- [ ] **Step 4: Move the view model**

```bash
git mv src/TrispotQR.App/ViewModels/MainViewModel.cs src/TrispotQR.ViewModels/MainViewModel.cs
```

Change its namespace to `TrispotQR.ViewModels` and drop the now-redundant `using TrispotQR.ViewModels;`. Make the timer required, since `WpfUiTimer` is in the app and no longer reachable:

```csharp
        _debounce = timer;
```

Change the parameter from `IUiTimer? timer = null` to `IUiTimer timer`. Update the app's construction site to pass `new WpfUiTimer()`, and every test that builds a `MainViewModel` to pass one. `MainViewModelTests` already has `FakeUiTimer` from Task 3.

If any WPF type remains in the file, the build now fails and names it. Fix by using the Core primitive, never by adding a reference back.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test TrispotQR.slnx`
Expected: PASS, 692.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Move MainViewModel to the shared project, with a test that keeps it neutral

The three couplings are gone, so it compiles without a UI toolkit. That is
asserted rather than assumed: one convenient using directive would undo it
on Windows, where nobody would notice."
```

---

## Task 7: Move the neutral view model tests and put them in CI

**Files:**
- Move: `tests/TrispotQR.Tests/EditorValidationTests.cs` into `tests/TrispotQR.ViewModels.Tests/`, and any part of `MainViewModelTests.cs` that needs no WPF
- Modify: `.github/workflows/ci.yml`, `README.md`

**Interfaces:**
- Consumes: Task 6's test project.
- Produces: a CI job running the view model tests on all three operating systems.

- [ ] **Step 1: Move what is genuinely neutral**

`EditorValidationTests.cs` tests `ContentEditors` and needed the app only for that namespace, so it moves whole. For `MainViewModelTests.cs`, judge each test: any that renders, touches the clipboard, or uses `StaThread` stays in `TrispotQR.Tests`. Move the rest. Start from:

```bash
grep -n "StaThread\|RenderTargetBitmap\|System.Windows" tests/TrispotQR.Tests/MainViewModelTests.cs
```

Splitting one file across two projects is fine. If it turns out to be more trouble than it is worth, leave the whole file where it is and say so in your report rather than forcing it.

- [ ] **Step 2: Run both test projects**

```bash
dotnet test tests/TrispotQR.ViewModels.Tests/TrispotQR.ViewModels.Tests.csproj
dotnet test tests/TrispotQR.Tests/TrispotQR.Tests.csproj
```

Expected: both PASS, and the totals still sum to what they did before the move.

- [ ] **Step 3: Add the project to CI**

In `.github/workflows/ci.yml` the `core` job restores, builds and tests one project. Give it both, and rename the job to match what it now covers:

```yaml
  portable:
    name: Core and view models on ${{ matrix.os }}
```

```yaml
      - name: Test Core
        run: dotnet test tests/TrispotQR.Core.Tests/TrispotQR.Core.Tests.csproj -c Release --verbosity normal

      - name: Test view models
        run: dotnet test tests/TrispotQR.ViewModels.Tests/TrispotQR.ViewModels.Tests.csproj -c Release --verbosity normal
```

Drop the separate restore and build steps and let `dotnet test` do both, so two projects do not each need their own copies.

- [ ] **Step 4: Update the README**

The "How it is put together" block gains the new project:

```
src\TrispotQR.Core\        encoding, styling, geometry, export, scannability checking
src\TrispotQR.ViewModels\  presentation logic, shared across UI toolkits
src\TrispotQR.App\         the WPF window and views
tests\                     three test projects; two run on Windows, Linux and macOS
```

Update the test count on the `dotnet test` line to what you measured.

- [ ] **Step 5: Commit and push**

```bash
git add -A
git commit -m "Run the view model tests on Linux and macOS too"
git push
```

- [ ] **Step 6: Watch CI**

Run: `gh run watch $(gh run list --limit 1 --json databaseId --jq '.[0].databaseId') --exit-status`
Expected: every job green. Read a failure closely: the last time this suite first ran on another platform it found a real assumption baked into a test, not a bug in the code.

---

## Self-review

**Spec coverage.** The spec's Phase 2 says the view models should survive the port "almost intact", with platform services behind interfaces. This plan does the extraction half: the shared project (1), dialogs behind an interface (2), timing behind an interface (3), the preview no longer a toolkit type (4), colour on Core's type (5), the view model moved and guarded (6), tests in CI (7). It also advances two mobile-readiness rules the Phase 1 review recorded as half-met, since storage and clipboard were already behind interfaces and this adds timing and the preview. Not covered here by design: the Avalonia app itself, which is 2b onward.

**Placeholders.** None. Every code step carries its code. Three steps deliberately say to inspect the tree rather than prescribing an exact edit, because the exact XAML binding lines, the thumbnail payload constant, and the split of `MainViewModelTests` cannot be known without looking. Each gives the command to run and a rule for deciding.

**Type consistency.** `IUiTimer` is defined in Task 3 and consumed in Tasks 3, 6 and 7. `PreviewDrawing` and `ThumbnailDrawing` are named in Task 4 and used in its own XAML step. The five colour properties keep their existing names in Task 5 and change only their type. `IDialogService` keeps all seven members. Expected totals rise 683, 683, 684, 686, 687, 692, and each task states its own.

**One risk worth naming.** Tasks 4 and 5 both edit `MainWindow.xaml` bindings, and a wrong binding does not fail the build. `MainWindowSmokeTests` fails on WPF binding errors and is the net. If it passes while the app looks wrong, suspect a converter returning null rather than throwing.
