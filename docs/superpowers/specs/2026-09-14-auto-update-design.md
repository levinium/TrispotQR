# Auto-update: design

Status: draft for review, 2026-09-14.
Reference implementation: [levinium/Mullion](https://github.com/levinium/Mullion), `src/Mullion.Core/Updates/*`,
`src/Mullion.App/Services/UpdateService.cs`, `UpdateInstaller.cs`, `.github/workflows/release.yml`.
Mullion is also Avalonia on .NET 10, so most of it ports directly. This document records what carries over,
what changes for TrispotQR, and why.

## Decisions already made

| Question | Decision |
| --- | --- |
| What a release contains | `TrispotQR.exe` (self-contained, single file) and `TrispotQR.exe.sha256`. Nothing else. Both zips and the framework-dependent build are dropped. |
| How releases are built | GitHub Actions on a pushed `v*` tag, producing a **draft** release for review. |
| What the notice offers | In-app **Update now**: download, verify, install, restart. |

## Goal

Someone running TrispotQR finds out a newer version exists without having to look, and can move to it
with one click, with no possibility of ending up with a broken or tampered app.

## Non-goals

- Code signing, and the Authenticode same-publisher check Mullion has ready for when it is signed. TrispotQR
  is unsigned; the check does nothing until it is, so it is left for the day signing happens.
- Installing on macOS or Linux. Neither is released. The check and the notice work everywhere; the install
  step is Windows only, and elsewhere the notice offers the download page.
- Checking repeatedly while the app stays open. TrispotQR is opened for a task and closed again, unlike
  Mullion's always-running tray app, so checking at launch is what matters.
- Delta updates, update channels, pre-release opt-in, automatic silent installs.

## How it works

### 1. The check

At launch, if the **Check for new versions** setting is on and the last check was at least a day ago, the app
makes one anonymous HTTPS GET to `https://api.github.com/repos/levinium/TrispotQR/releases/latest`. No body,
no query string, no identifier; the User-Agent names the product and version only. It never blocks startup
and never throws: offline, rate-limited, a proxy login page and malformed JSON all mean "could not tell",
which shows nothing.

The feed URL is baked into the build as assembly metadata (`UpdateFeedUrl`), defaulting to the project's own
releases. `-p:UpdateFeedUrl=` overrides it, which is how a fork points at its own releases, how an empty
value builds an app that never checks, and how the end-to-end test points a build at a local test server.

A release is offered only when its tag parses as `MAJOR.MINOR.PATCH`, it is not a draft or pre-release
(by flag or by a `-suffix` in the tag), and it is strictly newer than the running version, compared as
numbers so 1.10.0 beats 1.9.0. Anything unreadable resolves to "could not tell", never to a confident answer
either way. Four-part tags are refused rather than compared wrongly.

A manual **Check for updates** entry in the gear menu runs the same check immediately, ignoring the
schedule, and says what it found ("You have the latest version, v1.2.0" when there is nothing, "Could not
check for updates right now" when the question went unanswered). It works with automatic checking switched
off: the setting governs what the app does unasked, and a person asking is a different thing.

### 2. The notice

A banner at the top of the form, under the version label and gear, shown **only** when an update is
available. Nothing is ever on screen to say there is no news.

- *Available:* "Trispot QR 1.2.0 is available." with **Update now**, **What's new** (opens the release page)
  and **Later** (hides it until the next check, which is at most once a day).
- *Downloading:* a progress bar and "Downloading 42%".
- *Ready:* "Version 1.2.0 is ready." with **Restart now**, and the note that it also installs itself when
  TrispotQR is closed. Restarting clears whatever is typed in the content box, since content is not
  remembered between runs; the banner says so. Saved styles and settings carry over.
- *Cannot install here:* when the app sits somewhere it cannot write (Program Files, a read-only share), or
  is a development build, **Update now** becomes **Download**, which opens the release page.
- *Failed:* a one-line reason (download did not finish, checksum did not match) and the app carries on
  untouched.

Turning the setting off clears a notice the check produced (available or failed), but not a download the user
started.

### 3. The install

Carried over from Mullion unchanged in substance, because the order is the safety:

1. **Can it install at all?** A published single file (no `TrispotQR.Core.dll` beside the exe), on Windows,
   in a folder a write probe succeeds in. Asked before the button is offered.
2. **Stage.** Read the published checksum first, then stream `TrispotQR.exe` to `TrispotQR.exe.partial` beside
   the running exe, hashing as it goes, capped at 256 MB. A mismatch or a failed download deletes the file;
   only a match is renamed to `TrispotQR.exe.new`. Nothing about the installed app has changed at this point.
3. **Apply**, on Restart now or on close, and only for a `.new` this running copy staged and that still has
   the hash it verified. Windows will not let a running exe be overwritten but will let it be renamed: rename
   `TrispotQR.exe` to `TrispotQR.exe.old`, rename `.new` into its place, and if that second rename fails,
   rename the old one back. On Restart now, start the new exe with `--updated <pid>` and close the window
   normally so the session is saved.
4. **Clean up.** The new process waits for the old one to exit (only if that id still belongs to a
   TrispotQR process, and never failing the launch), then deletes `.old` and any stray `.new` or `.partial`.
   Fixed names, so a crash between any two steps leaves files the next launch recognizes.

Additions for TrispotQR:

- **HTTPS only**, for the feed and both downloads, except loopback addresses. The exception exists solely so
  the end-to-end test can run against a local server; a real address over plain HTTP is refused.
- **Install on close.** Mullion restarts immediately; TrispotQR offers that and also applies a staged update
  when the window closes, so nobody has to lose what they are typing to get the new version.

### 4. Releasing

`.github/workflows/release.yml`, adapted from Mullion's:

1. Triggered by pushing a `v*` tag.
2. Refuses if the tag is not `v` plus `<Version>` in `TrispotQR.Desktop.csproj`. Otherwise a build reporting an
   older version would ship, and every copy would keep offering the update it had just installed.
3. Runs `publish.ps1`, which runs all four test suites and refuses to produce an exe if any fail.
4. Writes `TrispotQR.exe.sha256` in `sha256sum` format.
5. Creates a **draft** release with both files and generated notes, for review, editing and publishing by
   hand.

`publish.ps1` drops the framework-dependent build and fails if anything other than `TrispotQR.exe` lands in
the output folder.

## Where the code goes

| Layer | Contents | Why there |
| --- | --- | --- |
| `TrispotQR.Core/Updates` | `ReleaseVersion`, `ReleaseFeed` (parsing GitHub's JSON), `UpdateDecision`, `UpdateAssets`, `UpdatePlan`, `UpdateSchedule` | Pure logic with no UI or platform, tested on all three OSes in CI. Every awkward case a feed can produce is reproducible without a network. |
| `TrispotQR.ViewModels` | `IUpdater`; update state, commands and schedule handling on `MainViewModel` | The notice's behavior is testable without a window. `IUpdater` is optional, so the WPF app passes nothing and simply has no updates. |
| `TrispotQR.UI/Services` | `GitHubUpdater : IUpdater` (HTTP, download, renames, launch) | Touches the network, the disk and processes. Its constructor takes the fetch, the exe path and the launcher as seams, so the rename sequence is tested against a scratch folder of stand-in files. |
| `TrispotQR.UI` | Banner in `MainWindow`, **Updates** card in `SettingsWindow`, gear menu entry | |
| `TrispotQR.Desktop/Program.cs` | Wait for the previous process and clean up leftovers, before Avalonia starts | Must run before anything could be holding the files. |
| `TrispotQR.Core/Presets/AppSettings` | `CheckForUpdates` (preference, default on), `LastUpdateCheckUtc` (session state) | |

## Privacy

What leaves the machine is one HTTPS request for a public file, at most once a day, plus the downloads if
the user clicks Update now. The README and the Settings card say so plainly. Nothing about the user, the
machine or the codes they make is sent or kept.

## Testing

- **Core** (all three OSes): version ordering including pre-release and the 1.10 vs 1.9 case; tag parsing
  including a leading `v`, build metadata and refused four-part tags; exact-name asset matching (the checksum
  file's name contains the exe's); checksum parsing with CRLF, BOM and `*` markers; case-insensitive hash
  comparison; every decision outcome; plan paths; the schedule, including a stored time in the future
  counting as due; feed parsing for drafts, pre-releases, assets still uploading, and an HTML proxy page.
- **UI services:** staging accepts a matching download and discards a mismatched one; apply performs both
  renames, rolls back when the second fails, and launches with `--updated`; cleanup removes leftovers;
  development-build and unwritable-folder detection; non-HTTPS refusal.
- **View model:** nothing shown when current or unknown; the version named when available; the setting off
  means no request and clears the notice; the schedule is respected and the check time written; Update now
  moves through downloading to ready; a failure reports and leaves the app as it was; a staged update
  applies on close.
- **Window:** the banner's visibility and text follow the view model; the Settings checkbox round-trips.
- **End to end, on this machine, before release:** a local HTTP server serves a fake release feed pointing at a
  higher-versioned published exe and its checksum. A published build with `-p:UpdateFeedUrl` aimed at it
  is run from a scratch folder, and we confirm in the real app that it offers the update, downloads,
  replaces its own exe, restarts reporting the new version and removes `.old`. Repeated with a wrong
  checksum (refused, nothing changed) and from a read-only folder (Download link instead).

## Rollout

- **Version.** The CHANGELOG's own rules make a new feature a MINOR bump, so this ships as **1.2.0**, not 1.1.1.
  It carries the clipboard fix as well.
- **Existing users.** Nobody on 1.0.0 or 1.1.0 has the updater, so reaching 1.2.0 takes one manual download.
  Every release after that arrives through the app.
- **README.** The Download section links `TrispotQR.exe` directly, explains the SmartScreen warning an unsigned
  exe gets, shows how to verify the checksum, and describes the update check.
