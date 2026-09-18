# Changelog

## How versioning works here

The version lives in exactly one place: `<Version>` in
`src\TrispotQR.Desktop\TrispotQR.Desktop.csproj`. Everything else reads it from the compiled
assembly, so the About window, the small label beside the gear, and the file properties of the
published `.exe` can never disagree.

That moved in 1.1.0, when the shipped app became the Avalonia one. `TrispotQR.App`, the WPF
build, still carries a `<Version>` of its own, but it now describes only itself and stays at
1.0.0 until that project is retired.

**To release a new version:**

1. Bump `<Version>` in `src\TrispotQR.Desktop\TrispotQR.Desktop.csproj`.
2. Add a section below describing what changed.
3. Commit, then tag and push: `git tag v1.2.3` and `git push origin main v1.2.3`.
4. The Release workflow refuses a tag that does not match the version, runs every test,
   builds `TrispotQR.exe` on Windows and the two Mac zips on a Mac, each with its checksum,
   starts the Mac app once to catch a crash at launch, and creates a **draft** release.
5. Edit the draft's notes, check the files, and publish it. Installed copies see it from then on.

`.\publish.ps1` builds the Windows files locally, and `bash tools/package-mac.sh` the Mac ones on
a Mac, for testing a build before tagging it.

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

## 1.3.0

**Trispot QR now runs on a Mac.** Download `TrispotQR-mac-arm64.zip` for Apple silicon (M1 and
later) or `TrispotQR-mac-x64.zip` for an Intel Mac, each holding `Trispot QR.app`, for macOS 12
(Monterey) or later. It is the same app as on Windows, built from the same code. It is not notarized
by Apple, so macOS asks you to allow it once, the first time it opens; the README says how. A Mac
copy tells you when a new version is out, but updating it means downloading the new version by hand.

**The appearance setting "Follow Windows" is now "Follow OS theme",** since it follows whichever
system the app is running on.

---

## 1.2.0

**Updates arrive through the app.** Once a day at launch Trispot QR asks GitHub whether a newer
version exists and shows a notice only if one does. Update now downloads it, verifies it against
the checksum published with the release, and swaps it in, either on Restart now or when the app
next closes, so nothing typed is lost. The check sends nothing about you or your machine. It can be
turned off in Settings, and run by hand from the gear menu. What's new shows the release notes in a
window inside the app rather than sending you to a browser.

**Downloads are now just `TrispotQR.exe`,** with a `.sha256` checksum beside it, rather than zips.
The framework-dependent build is discontinued: it carried native libraries beside the exe, which an
updater that replaces one file cannot keep in step.

**Fixed: with Trispot QR open, copy and paste could stop working across the whole machine.** A copied
QR code was left as a live object inside the app, so every other app's copy and paste had to go
through it, and any moment it was slow to answer broke clipboard use everywhere. The copied image
also vanished when the app closed. It is now written onto the clipboard properly and survives the
app closing.

Anyone on 1.0.0 or 1.1.0 needs to download this version once by hand; it is the first with the
updater.

---

## 1.1.0

**The app is now built on Avalonia rather than WPF.** It looks and behaves the same, reads
the same settings folder, and keeps your saved styles: upgrading is a straight replacement.
The reason for the change is Mac and Linux, which WPF cannot reach. Those builds are not
released yet, but CI now builds the app and runs its tests on all three operating systems.

The self-contained download is 45 MB, down from 65.

**Logos are saved with a style.** Saving a favorite used to drop its logo, because the only
thing it could record was where the file happened to sit on disk, and that path stops being
true the moment the file is renamed or the style is opened on another machine. The app now
keeps its own copy of the image, so the pairing survives. Applying a style brings its logo,
and applying one without a logo clears the current one.

Images are stored by a hash of their contents, so several styles sharing a logo keep one
copy between them, and copies nothing refers to any more are cleared out at startup.

**The logo controls moved out of Advanced options** into "How it looks", along with the
corner colors, which used to be separated from the code color they belong with.

**The color picker offers colors you are already using.** Two new rows: the colors present in
the code you are working on, and the ones you picked recently, kept across restarts. Matching
a second element to the first no longer means writing a hex code down.

**Favorites live in the strip of styles.** "Save this style" became a card at the end of the
row rather than a button elsewhere, and a saved style carries a small remove button.

**Fixes.**

- Copy reported success without saying so and, in Word, Outlook and Excel, without working.
  The confirmation was never shown at all, and the clipboard offered only PNG, which those
  applications will not paste. It now offers a plain bitmap as well, flattened onto white so
  they do not paste a black box.
- The confirmation itself was nearly invisible in dark mode: a near-black pill on a
  near-black page. It now inverts with the theme.
- Swatch outlines were too faint to see, particularly in dark mode.
- US spelling throughout the interface.

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
