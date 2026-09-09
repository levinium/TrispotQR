# Phase 2d: Styling panel and colour picker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the Avalonia app every control that changes how a code *looks* — shapes, gaps, colours, outline, error correction and margin — so a user can produce any styled code the WPF app can.

**Architecture:** The colour picker's interesting behaviour is not drawing, it is state: hue is *retained* when the colour goes grey, saturation and value round-trip, and hue wraps rather than clamping. That logic comes out of the WPF code-behind into a plain testable class first, and the Avalonia control is then a thin skin over it. The styling panel itself is ordinary bindings onto `MainViewModel` properties that already exist.

**Tech Stack:** Avalonia 12.1.2, .NET 10, xunit.v3 with `Avalonia.Headless.XUnit`.

**Spec:** `docs/superpowers/specs/2026-09-04-cross-platform-port-design.md`

## Where this sits

Phase 2b built the Avalonia shell; Phase 2c gave it all seven content types with live validation. The app can now describe any *content* but only in the default style.

This is the second of three remaining plans:
- **2c (done)** — the seven content editors and their validation.
- **2d (this one)** — the styling panel and the colour picker.
- **2e** — presets, logo, settings, theming, then retiring the WPF project.

## Global Constraints

- Target framework `net10.0`. Avalonia exactly `12.1.2`. SkiaSharp stays `4.151.2`. Do not change any package version.
- The existing WPF app keeps building and passing — 220 of the current 765 tests are its. This phase adds; it removes nothing from the WPF project.
- Do NOT change `MainViewModel`, `ContentEditor` or any subclass, `IDialogService`, `IUiTimer`, `IImageClipboard`, or anything in `TrispotQR.Core`. Every styling property this phase binds to already exists on `MainViewModel`.
- Compiled bindings are on, so every XAML file with bindings declares `x:DataType` (or `DataType` on a `DataTemplate`, which is the same thing). An unresolvable binding is a build error.
- The UI test project uses `xunit.v3` 3.2.2. `[AvaloniaFact]` / `[AvaloniaTheory]` for anything touching Avalonia layout or rendering; plain `[Fact]` / `[Theory]` for pure logic with no toolkit involvement.
- Test names are sentences describing behaviour; comments explain *why*, not *what*.
- No `Co-Authored-By` trailer on commits in this repository.

## Three Avalonia facts this codebase paid to learn

Carry these; do not rediscover them.

1. **Avalonia type selectors do not match subclasses.** `Selector="UserControl:error ..."` silently never matches a `FieldBox`. The working form is namespace-qualified: `local|FieldBox:error TextBox#Input`. It fails with no error and no warning, so if a style seems not to apply, suspect this first.
2. **`this.FindResource` does not exist**, and `TryFindResource` returns false before a control has a parent. `Application.Current.TryGetResource(key, ThemeVariant.Default, out var brush)` is what works.
3. **Avalonia's `TextBox` has no `VerticalScrollBarVisibility`.** The attached `ScrollViewer.SetVerticalScrollBarVisibility` is the equivalent.

And the standing lesson behind them: **this codebase has twice shipped a style or binding that did nothing while every test stayed green.** If you add a selector or a bound class, prove it applies by breaking it and watching a named test fail.

## What is NOT in this phase

- The presets strip and its thumbnails, the logo picker, the settings window, light and dark theming, About — Phase 2e.
- "Save this style as a preset", because it needs `IDialogService.AskForText`, which still returns null until 2e. The **Reset** button beside it IS in scope.
- Retiring the WPF app — 2e.
- The clipboard's second flattened-on-white entry, deferred from 2b — 2e.

## File Structure

```
src/TrispotQR.UI/
  Controls/HsvColor.cs                 Conversion between RgbColor and hue/saturation/value
  Controls/ColorPickerState.cs         The picker's behaviour, with no toolkit in it
  Controls/ColorPicker.axaml(.cs)      Swatch button plus the popup that edits a colour
  MainWindow.axaml                     The styling panel

tests/TrispotQR.UI.Tests/
  ColorPickerStateTests.cs             Plain [Fact] — no Avalonia needed
  ColorPickerTests.cs                  The control, headless
  StylingPanelTests.cs                 The panel wired to the view model
```

Why the state class is separate: the WPF original at `src/TrispotQR.App/Views/ColorPicker.xaml.cs` is 351 lines of code-behind, and its own test file spends 155 of its 241 lines testing pure colour arithmetic through a WPF control that has to be constructed on an STA thread to ask it a question about hue. That logic has nothing to do with any toolkit. Extracting it means those tests become plain unit tests that run everywhere in milliseconds, and the Avalonia control is left with only the part that genuinely needs a window.

---

### Task 1: The colour picker's behaviour, with no toolkit in it

**Files:**
- Create: `src/TrispotQR.UI/Controls/HsvColor.cs`
- Create: `src/TrispotQR.UI/Controls/ColorPickerState.cs`
- Test: `tests/TrispotQR.UI.Tests/ColorPickerStateTests.cs`

**Interfaces:**
- Consumes: `TrispotQR.Core.Primitives.RgbColor`, with `FromArgb(byte a, byte r, byte g, byte b)` and `A`/`R`/`G`/`B`.
- Produces: `TrispotQR.UI.Controls.HsvColor` — a `readonly record struct HsvColor(double Hue, double Saturation, double Value)` with `RgbColor ToRgb(byte alpha)` and `static HsvColor FromRgb(RgbColor)`. And `TrispotQR.UI.Controls.ColorPickerState` with `RgbColor Color { get; set; }`, `double Hue { get; }`, `double Saturation { get; }`, `double Value { get; }`, `void SetSaturationValue(double s, double v)`, `void SetHue(double degrees)`, and `bool TrySetHex(string text)`.

**The behaviour that matters**, all of it visible in the WPF original's tests at `tests/TrispotQR.Tests/ColorPickerTests.cs`:

- **Hue is retained through grey.** Drag brightness to zero and the colour becomes black; drag back and the *original hue* returns. Naively recomputing hue from RGB loses it, because black and white have no hue. The same applies to saturation: drag saturation to zero, and the hue must survive.
- **But an external colour does override the hue.** If the view model hands the picker a new colour that genuinely has a hue, the retained one is discarded. Only an external *grey* leaves it alone.
- **Hue wraps, it does not clamp.** 360 and 0 mean the same thing; -10 means 350.
- **Square coordinates clamp.** A drag outside the control is clamped to the edge, not ignored.

Read the WPF tests before writing yours. They encode real decisions, and the reasoning in their comments is worth carrying over.

- [ ] **Step 1: Write the failing tests**

`tests/TrispotQR.UI.Tests/ColorPickerStateTests.cs`. These need no Avalonia, so use plain `[Fact]` and `[Theory]`. Cover, at minimum, one test per bullet above, plus:

```csharp
    [Theory]
    [InlineData(0, 255, 0, 0)]
    [InlineData(120, 0, 255, 0)]
    [InlineData(240, 0, 0, 255)]
    public void MovingTheHueStripProducesTheExpectedColour(double hue, byte r, byte g, byte b)
    {
        // Fully saturated and fully bright, so the hue is the only variable and the expected
        // values are the primaries rather than something that needs its own arithmetic to check.
        var state = new ColorPickerState { Color = RgbColor.FromArgb(255, 255, 0, 0) };
        state.SetSaturationValue(1, 1);

        state.SetHue(hue);

        Assert.Equal(RgbColor.FromArgb(255, r, g, b), state.Color);
    }
```

Take the exact theory data for the round-trip and wrapping cases from the WPF file rather than inventing new numbers — that data was chosen to catch real arithmetic mistakes.

- [ ] **Step 2: Run them to watch them fail**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj --filter ColorPickerStateTests`
Expected: FAIL — neither type exists.

- [ ] **Step 3: Implement `HsvColor` and `ColorPickerState`**

Port the arithmetic from `src/TrispotQR.App/Views/ColorPicker.xaml.cs` — `AdoptHsvFrom`, `CommitHsv`, `Sync` and the hex parsing in `CommitHex`. Translate `System.Windows.Media.Color` to `RgbColor`; the conversion maths itself is unchanged.

The retained-hue rule lives in `ColorPickerState.Color`'s setter: when an incoming colour is grey (saturation effectively zero), keep the stored hue instead of recomputing it. Comment *why*, because a later reader will otherwise see it as a bug.

`TrySetHex` accepts what the WPF box accepted — check `CommitHex` for the exact forms, including whether a leading `#` is optional and whether 8-digit ARGB is allowed — and returns false rather than throwing on rubbish, since a user mid-type is not an error.

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`
Expected: PASS. Report the real total (71 before your additions).

- [ ] **Step 5: Commit**

```bash
git add src/TrispotQR.UI/Controls/HsvColor.cs src/TrispotQR.UI/Controls/ColorPickerState.cs tests/TrispotQR.UI.Tests/ColorPickerStateTests.cs
git commit -m "Extract the colour picker's behaviour from any toolkit"
```

---

### Task 2: The colour picker control

**Files:**
- Create: `src/TrispotQR.UI/Controls/ColorPicker.axaml`, `src/TrispotQR.UI/Controls/ColorPicker.axaml.cs`
- Test: `tests/TrispotQR.UI.Tests/ColorPickerTests.cs`

**Interfaces:**
- Consumes: `ColorPickerState` and `HsvColor` from Task 1; `AvaloniaGeometry.ToBrush` and `.ToColor` from Phase 2b.
- Produces: `TrispotQR.UI.Controls.ColorPicker`, a `UserControl` with `StyledProperty<RgbColor> SelectedColorProperty` (two-way by default) and `RgbColor SelectedColor { get; set; }`.

**Shape of it**, from `src/TrispotQR.App/Views/ColorPicker.xaml`: a swatch button showing the current colour over a checkerboard (so transparency reads as transparent), which opens a popup containing a saturation/value square, a hue strip beneath it, a row of palette swatches, a hex box, and RGBA sliders with live readouts.

**Bind `SelectedColor` as `RgbColor` directly.** The WPF version binds a `System.Windows.Media.Color` and converts at every call site with an `RgbToColor` converter; there is no reason to repeat that here, because `RgbColor` is already the platform-neutral type and Phase 2b's `AvaloniaGeometry` converts it where drawing needs it.

**FIVE THINGS TO SETTLE EMPIRICALLY.** I do not know these for Avalonia 12.1.2. Find out, do the right thing, and report what you found and how you confirmed it:

1. **`Popup` or `Flyout`?** WPF uses a `Popup` with `StaysOpen="False"`. Avalonia has both, and they differ in light-dismiss behaviour and in whether the headless harness can drive them.
2. **Pointer capture.** WPF's `CaptureMouse`/`ReleaseMouseCapture` on the square and strip becomes something else in Avalonia (`e.Pointer.Capture(control)`). The dragging behaviour — press inside, drag outside, keep tracking — must survive.
3. **Can the headless harness drive a drag at all?** `MessageWindowTests` already simulates `MouseDown`/`MouseUp` through real hit-testing. Whether pointer *move* works the same way decides how much of the drag behaviour can be tested through the control rather than through `ColorPickerState`.
4. **Does Avalonia's `Slider` move to the clicked point by default?** WPF's does not — `IsMoveToPointEnabled` defaults to false, which pages the value on a track click and reads as jumpy. The WPF suite has a whole file (`tests/TrispotQR.Tests/SliderBehaviourTests.cs`) walking the visual tree to guarantee every slider was fixed. Find out what Avalonia does; if it needs the same fix, apply it and carry a version of that guard into Task 3.

If a drag cannot be driven headlessly, say so plainly and cover the behaviour through `ColorPickerState` — which is exactly why Task 1 extracted it. Do not write a test that appears to drive the control but actually just calls the state class, and do not weaken an assertion to make a headless limitation look like a pass.

- [ ] **Step 1: Write the failing tests**

`tests/TrispotQR.UI.Tests/ColorPickerTests.cs`, using `[AvaloniaFact]`. Cover:

- The swatch shows the selected colour, asserting the rendered `Fill`/`Background`, not just that the property was set.
- Setting `SelectedColor` from outside updates the popup's parts.
- Editing through the control updates `SelectedColor`, and the binding carries it back out — a **round trip**, driven the way a user drives it as far as the harness allows.
- A transparent colour still shows the checkerboard rather than reading as white.

The round trip is the important one. Phase 2c found that no test in the branch drove the UI the way a user does, so a one-way binding would have been invisible. Do not repeat that: make at least one test go view → model, and prove it by making the binding one-way and watching exactly that test fail.

- [ ] **Step 2: Run them to watch them fail**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj --filter ColorPickerTests`
Expected: FAIL — `ColorPicker` does not exist.

- [ ] **Step 3: Build the control**

Port the layout from the WPF XAML. Keep the checkerboard behind the swatch: it is what makes a transparent background legible, and the app offers transparent backgrounds.

**The checkerboard is a fifth thing to settle empirically.** In WPF it is a `DrawingBrush` — see `Theme.Light.xaml:44` and `Theme.Dark.xaml:41`, which define it twice with different greys. **Avalonia has no `DrawingBrush`.** Work out the right equivalent (a tiled `ImageBrush`, a small `Canvas` behind the swatch, or something else), use it, and say what you chose and why.

Define it in `App.axaml` alongside `DangerBrush` and `WarningBrush`, under the name `CheckerBrush` so 2e can swap it per-theme in one place. Use the light-theme greys for now, since that is the only theme the Avalonia app has.

Keep the code-behind thin: it owns layout, pointer handling and the popup, and delegates every colour decision to `ColorPickerState`.

- [ ] **Step 4: Run everything and commit**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`, then `dotnet test TrispotQR.slnx -m:1`.

Note the `-m:1`: running four headless Avalonia hosts in parallel can exhaust the paging file and fail with `Failed to create CoreCLR, HRESULT: 0x8007000E`. CI is unaffected.

```bash
git add src/TrispotQR.UI/Controls/ColorPicker.axaml src/TrispotQR.UI/Controls/ColorPicker.axaml.cs src/TrispotQR.UI/App.axaml tests/TrispotQR.UI.Tests/ColorPickerTests.cs
git commit -m "Add the Avalonia colour picker"
```

---

### Task 3: The styling panel

**Files:**
- Modify: `src/TrispotQR.UI/MainWindow.axaml`
- Test: `tests/TrispotQR.UI.Tests/StylingPanelTests.cs`

**Interfaces:**
- Consumes: `ColorPicker` from Task 2, and these `MainViewModel` members, all of which already exist: `Foreground`, `BackgroundChoice`, `IsCustomBackground`, `CustomBackground`, `ModuleShape`/`ModuleShapes`, `ModuleScale`, `MarkerFrameShape`/`MarkerFrameShapes`, `MarkerCenterShape`/`MarkerCenterShapes`, `UseCustomMarkerColors`, `MarkerFrameColor`, `MarkerCenterColor`, `OutlineEnabled`, `OutlineColor`, `OutlineThickness`, `OutlineTarget`/`OutlineTargets`, `Ecc`/`EccLevels`, `QuietZone`, `PixelSize`, `ResetCommand`.

The WPF original is `src/TrispotQR.App/MainWindow.xaml` lines 403-520 (the "Advanced options" expander) plus the foreground and background controls in step 2. Match its **wording and its tooltips** — "A bigger gap looks lighter. Too big and the code stops scanning", "How much damage the code can survive…", "Clear space scanners need to find the code. 4 is the standard." Those sentences are the difference between a control someone can use and one they guess at.

Slider ranges, copied exactly: module scale 0.55-1.0; outline thickness 0.01-0.25; quiet zone 0-8 snapping to whole numbers.

The enum dropdowns reuse the `FriendlyEnumTemplate` and `FriendlyNameConverter` added in Phase 2c, so they need only `ItemTemplate="{StaticResource FriendlyEnumTemplate}"`. I have confirmed the template already exists at `MainWindow.axaml:25` and that the converter names all seven enum *types*, including this phase's six.

What I have NOT confirmed is that it names every *value* of each. Check that before wiring the dropdowns: bind each one, look at what it renders, and report any value that comes out as a bare identifier. A missing row shows as `RoundedSquare` instead of "Rounded square" — legible enough to slip through a review, wrong enough to notice in use.

**Two things NOT to include**, both Phase 2e: the logo controls (Choose image / Remove / size / punch shape), and "Save this style as a preset". `ResetCommand` is in scope.

- [ ] **Step 1: Write the failing tests**

`tests/TrispotQR.UI.Tests/StylingPanelTests.cs`. Cover:

- **Every styling control is reachable and bound.** Changing a control changes the view model — again, view → model, driven through the tree.
- **The conditional sections appear and disappear**: the marker colour row only when `UseCustomMarkerColors`, the outline detail only when `OutlineEnabled`, the custom background swatch only when `IsCustomBackground`. Assert on realised visibility, and check that hiding one does not leave a stale value that blocks export.
- **Every slider moves to the clicked point.** Model this on `tests/TrispotQR.Tests/SliderBehaviourTests.cs`, including its `BothTreesActuallyContainSliders` guard — a tree-walking test that finds no sliders passes vacuously, and that guard is what stops it. If Avalonia already behaves correctly by default, the test still earns its place: it is what catches a future slider added with its own style.
- **Reset restores the defaults** and the preview follows.

- [ ] **Step 2: Run them to watch them fail, then build the panel**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj --filter StylingPanelTests`
Expected: FAIL — the controls do not exist.

Then add the panel to `MainWindow.axaml`. Avalonia's `Expander` is the counterpart of WPF's for the collapsed "Advanced options" section; check its header and expansion API rather than assuming they match.

Watch the spacing convention Phase 2c settled on: spacing belongs to the container (`StackPanel.Spacing`, `Grid.RowSpacing`), not hand-written `Margin` on each child. A test in `MainWindowTests` asserts no field carries a margin of its own — keep it passing.

- [ ] **Step 3: Run everything**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj` — paste the output.
Run: `dotnet test TrispotQR.slnx -m:1` — report the per-project totals. The WPF project must still be exactly 220.

- [ ] **Step 4: Commit, push and report CI**

```bash
git add src/TrispotQR.UI/MainWindow.axaml tests/TrispotQR.UI.Tests/StylingPanelTests.cs
git commit -m "Add the styling panel to the Avalonia window"
git push
```

`gh` is at `C:\Program Files\GitHub CLI\gh.exe` and is NOT on PATH — invoke it by full path. CI triggers on any branch push. Report the per-job conclusions for all four jobs.

---

## Done when

- Every styling control from the WPF window, except the logo and save-preset, works in the Avalonia app and changes the live preview.
- The colour picker round-trips a colour both ways, and its behaviour — retained hue, wrapping, clamping — is covered by tests that need no window.
- Every slider moves to the point that was clicked, guarded by a test that cannot pass vacuously.
- CI green on all four jobs.
- The WPF app still builds and its 220 tests still pass.
- `MainViewModel`, the editors and Core are unchanged.
