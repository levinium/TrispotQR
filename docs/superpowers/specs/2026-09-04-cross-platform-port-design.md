# Trispot QR: cross-platform port

Status: proposed, awaiting review
Date: 2026-09-04

## Why

Trispot QR is a Windows-only WPF application. The request is to run it on macOS,
distributed as a `.dmg`, and on Linux, keeping the same standalone character it has on
Windows: copy one file, run it, nothing installed.

This is not a recompile. WPF exists only on Windows and always will. The renderer is built
directly on `System.Windows.Media`, so the port is a rewrite of the drawing layer and the
whole UI, on top of a body of logic that carries over untouched.

## Decisions taken

| Question | Answer |
|---|---|
| Scope | Full port. One Avalonia codebase replaces WPF on all three platforms |
| Windows | Ported too. The WPF app is retired rather than kept in parallel |
| macOS build | GitHub Actions `macos-latest` runner. No Mac hardware available |
| macOS signing | Unsigned for now. Notarisation deferred, and it changes no code |
| Repository | Public GitHub repo under the personal `levinium` account, kept separate from the work account on this machine. Actions is free on standard runners for public repos |
| Mobile | Not built, but actively designed for. See the rules below |

## What the port actually costs

Measured against the current tree, not estimated.

| Area | Lines | Fate |
|---|---|---|
| Payloads, QR encoding, presets, settings, shapes | ~1,200 | Move unchanged. No `System.Windows` at all |
| `QrStyle`, `StylePresets`, `HsvColor`, `ColorJsonConverter`, `ScannabilityChecker` | ~500 | Import `System.Windows.Media` only for the `Color` struct. Mechanical swap |
| `QrGeometryBuilder`, `ShapeFactory`, `QrDrawing`, `QrRenderer`, `LogoCompositor` | ~650 | Rewritten against a neutral geometry model |
| `PngExporter`, `SvgExporter`, `ClipboardExporter`, `QrDecoder` | ~420 | Rewritten |
| App: 2,490 C# and 1,345 XAML | ~3,835 | Ported to Avalonia |
| Tests | ~4,303 | Logic assertions survive. The UI harness is replaced |

Both NuGet dependencies are already cross-platform. `Net.Codecrete.QrCodeGenerator` is pure
managed code. ZXing is already called with a raw byte array
(`reader.Decode(pixels, w, h, BGR24)`), so only the code producing those bytes is WPF-bound.

## Architecture

### The decision everything else follows from

Avalonia's `RenderTargetBitmap.Render` requires the target to be attached to a window or a
headless session. WPF renders a detached `DrawingVisual` with nothing on screen, and that is
exactly how PNG export and the scannability check work today. SkiaSharp draws to an
offscreen surface with no window and no display at all.

So **Core rasterises with SkiaSharp and does not reference any UI framework**. Avalonia is
used for the on-screen preview and nothing else. This also keeps the door open for mobile,
where the same Core runs unchanged.

### Core, after the port

```
TrispotQR.Core            net10.0 (no -windows), SkiaSharp + ZXing + QrCodeGenerator
  Primitives/
    RgbColor.cs           replaces System.Windows.Media.Color
    QrPath.cs             figures of Move / Line / Cubic / Arc / Close, in module units
  Rendering/
    QrGeometryBuilder.cs  matrix + style -> QrPath layers (logic preserved)
    ShapeFactory.cs       module and marker shapes as QrPath (logic preserved)
    SkiaRasterizer.cs     QrPath -> SKSurface -> pixels. No window, no display
    LogoCompositor.cs     punch-out via Skia path ops
  Export/
    PngExporter.cs        SKImage encode
    SvgExporter.cs        writes d= straight from QrPath
  Validation/
    QrDecoder.cs          unchanged ZXing call, fed from Skia pixels
```

`QrPath` is the single geometry model. Preview, PNG and SVG all derive from it, which
strengthens the invariant the app already relies on: there is no second renderer that can
disagree with the first.

Two things disappear:

- `StaThread`. Apartment threading is a Windows concept, and nothing in a Skia pipeline
  needs it.
- The `SvgExporter` fill-rule trick. It currently strips WPF's `F0`/`F1` prefix off
  `Geometry.ToString()`, which is a WPF-specific debug-string behaviour. Writing SVG path
  data from `QrPath` directly is both portable and honest, and it removes a documented
  gotcha rather than porting it.

### App

```
TrispotQR.App             Avalonia, net10.0. Views, ViewModels, converters
TrispotQR.Desktop         entry point, one per-RID publish for win/osx/linux
```

The ViewModels survive almost intact: they are already framework-neutral apart from the
`Color` type and `DispatcherTimer`. The XAML maps over closely, with known differences:

| WPF | Avalonia |
|---|---|
| `Style TargetType="X"` | selector syntax, `Style Selector="X"` |
| `Trigger` / `DataTrigger` | pseudo-classes and `:` selectors, or bindings |
| `DynamicResource` | same, and there is a native `ThemeVariant` for light/dark |
| `IValueConverter` | same interface |
| Implicit `DataTemplate` by type | `DataTemplates` with `DataType` |

Platform services get interfaces in the app and per-platform implementations where they
differ: file dialogs via Avalonia's `IStorageProvider`, and clipboard via `IClipboard`.

### Preview rendering

`QrPath` converts to an Avalonia `StreamGeometry` for the live preview, so the on-screen
code stays vector-crisp at any window size. Export never goes through this path.

## Packaging

| Platform | Output | Built on |
|---|---|---|
| Windows | single-file self-contained `.exe`, as now | `windows-latest` |
| Linux | self-contained binary plus AppImage | `ubuntu-latest` |
| macOS | `.app` bundle (Info.plist, `.icns`, binary) wrapped by `hdiutil` into a `.dmg` | `macos-latest` |

macOS ships unsigned. A downloaded unsigned app is quarantined by Gatekeeper and reports
itself as damaged, so the download needs a short instruction to right-click and choose Open
the first time. Notarisation later requires an Apple Developer account and changes no code,
only the workflow.

Standard runners only. GitHub's larger runners, including the Apple Silicon macOS ones, are
billed even on public repositories.

## Testing

The existing 599 tests split three ways.

- **Core logic** (payloads, validation, encoding, presets) moves with no changes.
- **Rendering and export** keeps its assertions and changes its harness. The style matrix,
  which renders every shape combination and decodes it back, is the guardrail for the whole
  port: if a Skia-rendered code decodes to the same string, the renderer is correct.
- **UI tests** move to `Avalonia.Headless.XUnit`. This is a straight improvement on the
  current STA-thread harness: `[AvaloniaFact]` handles threading, windows can be shown
  headlessly so `RenderTargetBitmap` works, and it can simulate real input.

One new test matters more than the rest: **render the same payload and style through the
WPF renderer and the Skia renderer and assert both decode to the same string**. Run during
Phase 1 while both exist, it proves the new renderer is faithful before any UI work starts.
Pixel-identical output is explicitly not the goal, since antialiasing differs between
engines. Scanning correctly is.

## Phasing

Each phase is independently verifiable on Windows, which is the only platform available
here.

**Phase 1: Core loses WPF.** Add `RgbColor`, `QrPath`, `SkiaRasterizer`. Rewrite the
exporters and decoder against them. The existing WPF app keeps running on top, via a
throwaway `QrPath`-to-WPF-geometry adapter that lives **in the WPF app project, never in
Core**, and is deleted with it in Phase 2. Ends with the full suite green and the app
behaving identically. This is the hard part, and it happens with zero UI risk.

**Phase 2: Avalonia app.** Port views and XAML, move UI tests to the headless harness, reach
feature parity on Windows, delete the WPF project and its adapter.

**Phase 3: CI and the other two platforms.** Initialise git, publish to GitHub, add the
workflow, produce Linux and macOS artefacts. This is the first point where anything cannot
be verified from this machine.

## Risks

| Risk | Handling |
|---|---|
| Clipboard image formats differ per OS | Windows behaviour is already understood and tested. macOS and Linux need real testing on the target. Worst case, the platform falls back to save-to-file |
| Native file dialogs behave differently | `IStorageProvider` abstracts this, but the result needs checking on each platform |
| Skia renders shapes subtly differently from WPF | The style matrix and the cross-renderer decode test cover it. Visual differences that still scan are acceptable |
| Unsigned `.dmg` blocked by Gatekeeper | Documented instruction now, notarisation later |
| macOS and Linux ship without hands-on testing | Real limitation. Recommend a colleague on each platform smoke tests before wider distribution |
| UI font metrics differ across platforms | Layout uses relative sizing. The QR output is geometry and is unaffected |

## Mobile: designed for, not built

Android and iOS are a wanted future target. Nothing here builds them, and everything here is
built so they stay cheap to add. Avalonia targets both from the same codebase, and a Core
with no UI dependency runs on both unchanged.

What a mobile version would and would not reuse:

| Layer | On mobile |
|---|---|
| Core: payloads, encoding, validation, geometry, Skia rasterising, export | Reused as-is |
| ViewModels | Reused, provided they stay free of desktop assumptions |
| Views and XAML | Rewritten. A 1180x800 two-column layout does not fit a phone |
| File saving, clipboard, dialogs | Per-platform implementations behind the same interfaces |

### Rules to follow from Phase 1 so this stays true

These cost nothing now and are expensive to retrofit:

1. **Core takes and returns bytes and streams, never paths or dialogs.** Anything that
   chooses a location belongs to the app. Mobile has no file picker in the desktop sense:
   iOS uses a share sheet, Android uses the Storage Access Framework.
2. **Settings and preset storage go behind an interface**, not a hard-coded folder. Desktop
   resolves per-OS; mobile resolves to the app sandbox. This is already an open item below.
3. **ViewModels must not assume a window, a pointer, or a fixed size.** No hover-only
   affordances carrying meaning, no right-click as the only route to an action, no pixel
   dimensions baked into view models.
4. **Clipboard and export sit behind the same service interfaces** as file dialogs, so a
   mobile implementation is a new class rather than a change to shared code.
5. **Keep views in their own project or folder**, separate from ViewModels, so a mobile head
   can be added beside the desktop one rather than carved out of it.
6. **Do not reference `Avalonia.Desktop` from shared code.** Desktop-only APIs belong in the
   desktop entry point.

### What would still be needed later

- A phone and tablet UI: single column, touch targets, share sheet instead of Save As.
- Android ships as an APK you can hand out directly, or through the Play Store for $25 once.
- iOS requires an Apple Developer account at $99/year plus App Store or TestFlight review.
  There is no sideloading route for ordinary users, so Android is substantially cheaper to
  reach than iOS.

## Also out of scope for now

- **macOS notarisation.** Deferred, workflow-only when wanted, changes no code.
- **Feature changes.** The port is behaviour-preserving. Anything new waits.

## Repository and identity

The project is published under the personal `levinium` account. This machine also carries
work repositories, so the two identities must not be able to cross.

The separation is per repository, with nothing set globally:

- Commit identity is set **local to this repository**, so no other project inherits it.
- Authentication uses a **dedicated SSH key** reached through a `github-levinium` host alias
  in `~/.ssh/config`, with `IdentitiesOnly yes`. The remote URL names that alias, so this
  repository cannot authenticate as anything else, and no other repository can pick this key
  up by accident.
- No global `user.name`, `user.email` or credential helper is set, and none exists today.
  Other projects declare their own identity, which is the behaviour that makes a wrong
  attribution impossible rather than merely unlikely.

`gh` is not installed and is not required. The repository is created in the browser and the
remote is added by hand.

## Open items

1. The version number for the ported release. Suggest 2.0.0, since retiring WPF and
   changing the renderer justifies a major bump under the scheme in CHANGELOG.md.
2. Whether saved styles from the Windows version should be readable by the ported app. The
   JSON is unchanged, so this comes free provided storage moves behind an interface, which
   mobile rule 2 above requires anyway. Desktop resolves per-OS: `%APPDATA%` on Windows,
   `~/Library/Application Support` on macOS, `$XDG_CONFIG_HOME` on Linux.
