# Phase 2e: the remaining Avalonia windows — manual verification checklist

Written 2026-09-10, at the end of Phase 2e's final fix wave. An agent cannot verify a GUI
by eye, so these are left unticked rather than guessed at. They are the nine checks from
the plan's Verification section, plus two that this branch escalated to the list because
no automated test can reach them.

Run:

```
dotnet run --project src/TrispotQR.Desktop/TrispotQR.Desktop.csproj
```

**Step 10 is the most important one on this list.** It is the single claim that justifies
deleting WPF's Windows registry read, it is demonstrated by no test at all, and it is
the one thing here that cannot be checked on a machine set to light.

## From the plan

- [ ] 1. Launch, click each preset card, confirm the preview changes and the badge stays
      green.
- [ ] 2. Save a style with a name; confirm it appears in the strip and survives a restart.
- [ ] 3. Right-click a built-in preset — no delete option. Right-click the saved one —
      delete works.
- [ ] 4. Add a logo, confirm the "raise error correction" button appears, click it, confirm
      the code still scans on a phone.
- [ ] 5. Switch to Dark in Settings; confirm the whole window changes, including the
      checkerboard behind the preview, and that the QR code's own colours do not.

      The checkerboard is now really there — it was missing when the plan was written, and
      was ported in this fix wave. To see it as a checkerboard rather than as a flat
      panel, set the code's background to Transparent first. It has its own muted dark
      variant, so it should read as "nothing here" against the dark window rather than as
      a lit panel.

      **Still in Dark, open a colour picker** — click the swatch beside "Code colour", or
      any other swatch on the page — and confirm the popup card is dark too: a dark
      ground, an edge you can see against it, and readable captions above the square, the
      presets and the hex box.
      This step is called out because the whole window can change while the popup stays a
      white card floating over it; that is exactly the defect the last fix wave shipped,
      and nothing in step 5 as originally written would have caught it. The saturation
      square and the hue rainbow inside the popup are colour space, not chrome, and are
      supposed to look identical in both appearances.
- [ ] 6. Open Settings, switch to Dark, close with the title-bar X; confirm the theme goes
      back.
- [ ] 7. Set a default save folder; confirm the next Save opens there.
- [ ] 8. Open About; confirm the version matches the csproj.
- [ ] 9. Resize the window small enough to scroll and confirm the scrollbar still clears
      the form (the Phase 2d fix, re-checked now that the panel is taller).

## Escalated to this list

- [ ] 10. **"Follow Windows" really follows Windows.** On a desktop set to dark, launch
      with the appearance set to Follow Windows and confirm the app comes up dark; switch
      the OS to light while it is running and confirm the app follows without a restart.

      This is the one claim justifying the deletion of WPF's registry read
      (`src/TrispotQR.App/App.xaml.cs:23-24`): Avalonia's `ThemeVariant.Default` is
      documented to mean "follow the operating system" on every platform, and
      `ThemeSwitcher` maps Follow Windows straight onto it. No test here demonstrates it,
      because the headless platform has no OS appearance to follow. Until this is checked
      by hand, the cross-platform simplification rests on documentation alone.

      Worth repeating on macOS if the machine is available, since that is the other half of
      what the registry read could never have done.
- [ ] 11. **Dark survives a restart.** Choose Dark in Settings, press Done, quit the app,
      and launch it again. It must come up dark, without a light flash first.

      Added because it was broken until this fix wave: the choice was written to
      settings.json and nothing ever read it back. The flash matters as well as the colour
      — the theme is applied before the first window is constructed, and a visible repaint
      would say that ordering had regressed.

## Carried to Phase 2f

- **`ConfirmRisk`'s `severe` parameter is unimplemented.** It reaches
  `AvaloniaDialogService.ConfirmRisk` and is dropped: the Avalonia `MessageWindow` is a
  title, a body and two buttons, with no heading element to paint. What it is supposed to
  look like is specified in exactly one place, **`src/TrispotQR.App/Views/ConfirmWindow.xaml.cs:27`**,
  which paints the badge `#C5221F` when severe and `#E38C00` otherwise. Phase 2f deletes
  that file, so either port the behaviour or record the two colours before it goes.

  Nothing is lost meanwhile: the heading text and the default button, both derived by
  `MainViewModel` from the same verdict, already differ between a code that did not scan
  and one that merely might not.

Anything that fails is a finding to report, not something to patch over.
