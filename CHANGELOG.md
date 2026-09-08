# Changelog

## How versioning works here

The version lives in exactly one place: `<Version>` in `src\TrispotQR.App\TrispotQR.App.csproj`.
Everything else reads it from the compiled assembly, so the About window, the small label
beside the gear, and the file properties of the published `.exe` can never disagree.

**To release a new version:**

1. Bump `<Version>` in `src\TrispotQR.App\TrispotQR.App.csproj`.
2. Add a section below describing what changed.
3. Run `.\publish.ps1`, which runs the tests first and refuses to publish if any fail. It
   prints the version it built, so what you hand over is always identifiable.
4. Copy `dist\TrispotQR.exe` wherever it is going.

Numbering follows the usual three parts, `MAJOR.MINOR.PATCH`:

- **PATCH** for fixes that change nothing about how the app is used.
- **MINOR** for new features that do not break existing saved styles or settings.
- **MAJOR** for anything that would invalidate a saved style, a settings file, or an
  established habit.

Saved styles and settings live in `%APPDATA%\TrispotQR`. A file that cannot be read is moved
aside rather than deleted, so a version that changes their format degrades to defaults
instead of losing someone's work.

That folder was called `Trispot` before the app was renamed. Anything left there is copied
across once, on first run, and the old folder is left alone rather than moved.

---

## 1.0.0

First release.

**Making codes.** Any text, or one of the guided types: Link, Wi-Fi, Email, Phone, Text
message, Contact card. The Link type adds `https://` when it is missing and warns when what
you typed is not a usable web address.

**Styling.** Six built-in looks plus full control underneath: dot shape, corner ring and
centre shapes, the gap between dots, outlines, separate colours for the corner markers,
error correction level, margin size, and a logo in the middle. Styles can be saved as named
presets.

**Colour picker.** A saturation and value square with a hue strip, a curated palette, a hex
box and RGB sliders, all kept in step with each other.

**Checking what you typed.** Each content type knows what it needs. A field that is missing
or malformed is outlined in red with the reason underneath it, as you type, and the Save and
Copy buttons stay disabled until it is fixed, saying how many problems are left. A Wi-Fi key
of an unusual length is a warning rather than a block, because routers vary.

**Checking that it scans.** After every change the code is rendered and read back with a real
barcode decoder. A badge reports whether it scans, and low contrast, an inverted palette, a
missing margin and an oversized logo are flagged even when the decode succeeds. Exporting or
saving a style that does not scan asks for confirmation first.

**Saving.** PNG at three sizes or a custom one, SVG for print, or straight to the clipboard.

**Settings.** Appearance (follow Windows, light or dark), whether to warn about codes that
may not scan, a fixed save folder, the size new codes start at, and whether the last style
is restored on reopen.

**Under the hood.** The drawing engine does not depend on Windows. Rendering goes through
SkiaSharp and the geometry model is the app's own rather than WPF's, which is the groundwork
for the macOS and Linux versions. Nothing about using the app changes.

One narrowing worth recording: the colour parser recognises a handful of named colours
(black, white, transparent, red, green, blue, gray/grey) rather than WPF's list of roughly
140, and no longer accepts `#RGB` shorthand. That only affects a preset file someone
hand-edited to use one of those. Every file the app itself writes uses `#RRGGBB` or
`#AARRGGBB` and round-trips unchanged.
