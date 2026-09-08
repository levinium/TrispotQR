# Phase 2b: Avalonia shell — manual verification checklist

Written 2026-09-08, at the end of Phase 2b. An agent cannot verify a GUI by eye, so
these steps were deliberately left unticked rather than guessed at. Step 1 is the one
that matters most: it is the single scenario the automated suite structurally cannot
reach, and it is the one that was actually broken before the final review caught it.


```
dotnet run --project src/TrispotQR.Desktop/TrispotQR.Desktop.csproj
```

and walk through, unticked. **Step 1 is the most important one on this list** — it is the single
scenario the automated suite structurally cannot reach, and the one that was actually broken:

- [ ] 1. **The modal dialog answers a click.** Choose "Plain text" and paste in enough content to
      make the scannability badge go amber (a long payload at low error correction does it —
      roughly 900+ characters of Lorem Ipsum; keep adding until the badge stops being green).
      Click Save PNG. A confirmation dialog must appear reading "This code may not scan", and it
      must **respond to a click** — the window drags, the buttons highlight on hover, and clicking
      "Save anyway" or "Cancel" dismisses it immediately. If the dialog appears but is frozen and
      the whole app is unresponsive, the dispatcher pump has regressed to ignoring OS events; it
      would eventually throw `TimeoutException` after ten minutes and take the app down. Check
      both buttons, and check the title-bar X, which is the same OS-event path.

      A Windows-only automated version of this check lives at `tools/DispatcherProbe` and can be
      run with `dotnet run -c Release -- old` and `dotnet run -c Release -- new`, but it does not
      cover the macOS or Linux backends, and it drives a bare `MessageWindow` rather than the real
      save flow. This step still needs doing by hand, ideally on a Mac.

- [ ] 2. The window opens at the size the previous session left it (resize it, close, reopen), and
      at 1180x800 the very first time — not always the 1000x700 in the XAML.
- [ ] 3. Choose "Plain text" and type `https://www.emanuelnyc.org`. The preview appears within
      about a second and the badge goes green.
- [ ] 4. Resize the window larger. The code stays sharp rather than going blocky (vector preview),
      and the resize itself stays smooth.
- [ ] 5. Click Save PNG. A real save dialog opens, defaulting to a `.png` name. Save it, open the
      file, confirm it is the same code.
- [ ] 6. Click Save SVG, save it, open it in a browser, confirm it matches.
- [ ] 7. Click Copy, then paste into any editor that accepts an image.
- [ ] 8. Switch the content type to Wi-Fi. The placeholder text appears rather than an empty
      panel.
- [ ] 9. Switch back to Plain text. The typed text is still there.
- [ ] 10. Corrupt `%APPDATA%\TrispotQR\presets.json` (write `{` into it), then launch. The app
      must open its window and *then* show a "Saved styles" message, rather than dying at startup.
      Restore the file afterwards.
- [ ] 11. Close the window. It closes without an error.

Anything that fails is a finding to report, not something to patch over.

