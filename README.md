# Trispot QR

A standalone Windows app for making QR codes. Type anything in, style it, and save it as a
PNG (with a transparent or solid background) or as an SVG for print.

Nothing is sent anywhere. The code is generated on the machine it runs on, which is the
main reason to use this rather than a website: online generators see your content, and many
of them quietly turn your code into a redirect through their own servers that stops working
the day they do.

## Running it

Copy `dist\TrispotQR.exe` anywhere and double-click it. Nothing needs to be installed, and
nothing gets installed: there is no setup step, no admin prompt and no registry entry. The
whole app, including the .NET runtime it runs on, is inside that one file, which is why it
is around 67 MB. Delete the file and it is gone.

It needs 64-bit Windows and nothing else.

`dist\framework-dependent\TrispotQR.exe` is the same app at around 13 MB, for machines that
already have the .NET 10 Desktop Runtime: its size difference from the big build is the .NET
runtime, not the drawing engine, since the native Skia library ships in both. If the runtime
is missing, Windows shows a dialog naming it with a download link; it does not install
anything on its own. Prefer the big one unless you are deploying somewhere the runtime is
already managed.

## What it does

**Content.** Plain text, or one of the guided types: Link, Wi-Fi, Email, Phone, Text
message, Contact card. Each one builds the exact payload phones expect. The Link type adds
`https://` when it is missing and warns when what you typed is not a usable web address,
which is the single most common way a QR code ends up scanning perfectly and doing nothing.
Every type checks what you typed as you type it: a missing or malformed field is outlined in
red with the reason underneath, and saving stays disabled until it is fixed.

**Style.** Six built-in looks, and full control underneath: shape of the dots, shape of the
corner rings and their centres, a gap between dots, outlines, separate colours for the
corner markers, error correction level, margin size, and a logo in the middle.

**Checking.** After every change the app renders the code and reads it back with a real
barcode decoder, then shows a badge: green for scannable, amber when something about it is
risky. Low contrast, an inverted palette and a missing margin get flagged even when the
decode succeeds, because those are the codes that read on a monitor and then fail on a
printed flyer.

**Saving.** PNG at three sizes or a custom one, SVG for print, or straight to the clipboard
for pasting into Word, PowerPoint or an email.

## Anything still worth checking by hand

The app's own decoder is strict but it is not a phone camera. Before a code goes to print,
scan the saved file with an actual phone. That is the only test that fully counts.

## Building from source

Needs the .NET 10 SDK.

```powershell
dotnet test          # 681+ tests
.\publish.ps1        # builds dist\TrispotQR.exe
```

`publish.ps1` runs the tests first and refuses to publish if any fail.

## How it is put together

```
src\TrispotQR.Core\    encoding, styling, geometry, export, scannability checking
src\TrispotQR.App\     the WPF window and view models
tests\TrispotQR.Tests\ the test suite
tools\               one-off build utilities (the app icon generator)
```

### The design that matters

Everything is drawn once into Core's own platform-neutral geometry model (`QrPath`), measured
in **module units**, one unit per QR module. That single description then feeds the
on-screen preview, the Skia rasteriser, and the SVG writer. There is no second renderer to
drift out of sync, which is why the SVG and the PNG always match what the preview showed.

### Why the test suite is shaped the way it is

The scannability matrix in `StyleMatrixScanTests` renders every combination the UI can
produce and decodes it back. It is not a formality. It caught a diamond-shaped corner
marker that looked good and failed to scan in every single configuration, because scanners
locate a code by the 1:1:3:1:1 run of dark and light through its corner markers and a
diamond breaks that ratio. That option was removed rather than shipped. Run this matrix
before believing any change to the renderer is safe.

`MainWindowSmokeTests` lays out the real window offscreen and fails on WPF data binding
errors, which are otherwise invisible: a mistyped binding path shows an empty control and
writes a line to a trace listener nobody reads.

### Where settings live

`%APPDATA%\TrispotQR\` on Windows holds `presets.json` (saved styles) and `settings.json`
(last used style and window size). The location resolves per platform (`~/Library/Application
Support/TrispotQR` on macOS, `$XDG_CONFIG_HOME/TrispotQR` or `~/.config/TrispotQR` on Linux),
though only the Windows app ships in this phase. A file that will not parse is moved aside
and the app starts on the built-in styles rather than refusing to open.

## Third-party components

| Package | Licence | Used for |
| --- | --- | --- |
| Net.Codecrete.QrCodeGenerator | MIT | QR encoding |
| ZXing.Net | Apache 2.0 | decoding, for the scannability check |
