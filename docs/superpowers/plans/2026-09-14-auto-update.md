# Auto-update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** TrispotQR checks GitHub for a newer release at launch, shows a notice only when one exists, and can download, verify and install it in place.

**Architecture:** Pure update logic (version ordering, feed parsing, decision, asset selection, file plan, schedule) lives in `TrispotQR.Core/Updates` and is tested on all three OSes. `MainViewModel` owns the notice state machine against an optional `IUpdater`. `TrispotQR.UI/Services/GitHubUpdater` does HTTP, download, checksum and the rename-aside install, with seams for tests. A GitHub Actions workflow publishes `TrispotQR.exe` plus `TrispotQR.exe.sha256` as a draft release on a `v*` tag.

**Tech Stack:** .NET 10, Avalonia 12.1.2, xUnit 2.9 (Core, ViewModels), xunit.v3 3.2.2 + Avalonia.Headless.XUnit (UI), GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-14-auto-update-design.md`. Reference implementation: github.com/levinium/Mullion (`src/Mullion.Core/Updates/*`, `src/Mullion.App/Services/UpdateService.cs`, `UpdateInstaller.cs`, `.github/workflows/release.yml`).

## Global Constraints

- Work on branch `auto-update`. Never commit with a `Co-Authored-By` trailer.
- Never use `--` inside an XML or XAML comment. A failed build leaves a stale binary; after any build error, fix and rebuild before trusting any test result.
- Feed URL default: `https://api.github.com/repos/levinium/TrispotQR/releases/latest`. Release page default: `https://github.com/levinium/TrispotQR/releases/latest`.
- Release assets: exactly `TrispotQR.exe` and `TrispotQR.exe.sha256` (sha256sum format: `<hex>  TrispotQR.exe`).
- Download cap: 256 MB. Check interval: 1 day. HTTP timeout for the check: 10 seconds.
- HTTPS only for feed and downloads, except loopback hosts (`localhost`, `127.0.0.1`, `::1`).
- Staged file `TrispotQR.exe.new`, backup `TrispotQR.exe.old`, restart argument `--updated <pid>`.
- The check never throws and never blocks startup. Unreadable input resolves to `Unknown`, never to a confident answer.
- Drafts and pre-releases (flag or `-suffix` in tag) are never offered. Four-part tags are refused.
- The notice is shown only when an update is available. Nothing on screen says "no news".
- User-facing text is US English, no em dashes.
- Version for this release: `1.2.0` in `src/TrispotQR.Desktop/TrispotQR.Desktop.csproj`.
- The WPF app (`TrispotQR.App`) receives no updater and must keep compiling unchanged.
- Sample data in tests uses `example.org`. No organization names anywhere.

## File map

| File | Responsibility |
| --- | --- |
| `src/TrispotQR.Core/Updates/ReleaseVersion.cs` | Parse and order `MAJOR.MINOR.PATCH[-pre]` |
| `src/TrispotQR.Core/Updates/ReleaseFeed.cs` | `ReleaseAsset`, `ReleaseInfo`, parse GitHub release JSON |
| `src/TrispotQR.Core/Updates/UpdateDecision.cs` | `UpdateOutcome`, `UpdateVerdict`, decide whether to offer |
| `src/TrispotQR.Core/Updates/UpdateAssets.cs` | Pick exe and checksum, read checksum, compare hashes, URL rule |
| `src/TrispotQR.Core/Updates/UpdatePlan.cs` | Current / staged / backup paths |
| `src/TrispotQR.Core/Updates/UpdateSchedule.cs` | Whether a check is due |
| `src/TrispotQR.Core/Presets/AppSettings.cs` | `CheckForUpdates`, `LastUpdateCheckUtc` |
| `src/TrispotQR.ViewModels/IUpdater.cs` | Contract the view model drives |
| `src/TrispotQR.ViewModels/MainViewModel.Updates.cs` | Notice state, commands, schedule (partial class) |
| `src/TrispotQR.UI/Services/GitHubUpdater.cs` | HTTP, download, verify, install, launch, cleanup |
| `src/TrispotQR.UI/Services/UpdateStartup.cs` | Wait for predecessor, remove leftovers |
| `src/TrispotQR.Desktop/Program.cs` | Calls `UpdateStartup` before Avalonia |
| `src/TrispotQR.Desktop/TrispotQR.Desktop.csproj` | Version 1.2.0, `UpdateFeedUrl`/`UpdatePageUrl` metadata |
| `src/TrispotQR.UI/MainWindow.axaml(.cs)` | Banner, gear menu entry, composition, close/restart |
| `src/TrispotQR.UI/Views/SettingsWindow.axaml(.cs)` | Updates card |
| `publish.ps1`, `.github/workflows/release.yml` | Single-exe publish, tagged draft release |
| `README.md`, `CHANGELOG.md` | Download, verification, update check, 1.2.0 notes |

---

### Task 1: ReleaseVersion

**Files:**
- Create: `src/TrispotQR.Core/Updates/ReleaseVersion.cs`
- Test: `tests/TrispotQR.Core.Tests/Updates/ReleaseVersionTests.cs`

**Interfaces:**
- Produces: `public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, string? PreRelease) : IComparable<ReleaseVersion>` with `bool IsPreRelease`, `static bool TryParse(string? text, out ReleaseVersion version)`, `bool IsNewerThan(ReleaseVersion other)`, `int CompareTo(ReleaseVersion other)`, `ToString()` giving `1.2.0` or `1.2.0-rc.1`.

- [ ] **Step 1: Write the failing tests**

```csharp
using TrispotQR.Core.Updates;

namespace TrispotQR.Tests.Updates;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData(" V1.2.3 ", 1, 2, 3, null)]
    [InlineData("1.2", 1, 2, 0, null)]
    [InlineData("1", 1, 0, 0, null)]
    [InlineData("1.2.3-rc.1", 1, 2, 3, "rc.1")]
    [InlineData("1.2.3+abc123", 1, 2, 3, null)]
    public void ParsesTheFormsATagCanTake(string text, int major, int minor, int patch, string? pre)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var v));
        Assert.Equal(new ReleaseVersion(major, minor, patch, pre), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    [InlineData("-1.2.3")]
    [InlineData("1.0.0.1")]
    public void RefusesWhatItCannotOrderSafely(string? text)
    {
        // A four-part tag is refused rather than truncated: 1.0.0.1 and 1.0.0.2 would compare
        // equal, and an update between them would be silently missed.
        Assert.False(ReleaseVersion.TryParse(text, out _));
    }

    [Fact]
    public void ComparesNumbersAsNumbers()
    {
        // As text, "1.10.0" sorts before "1.9.0", which would tell someone on 1.9.0 they are
        // up to date forever.
        Assert.True(Parse("1.10.0").IsNewerThan(Parse("1.9.0")));
    }

    [Fact]
    public void AFinishedReleaseIsNewerThanItsOwnReleaseCandidate()
    {
        Assert.True(Parse("1.2.0").IsNewerThan(Parse("1.2.0-rc.1")));
        Assert.False(Parse("1.2.0-rc.1").IsNewerThan(Parse("1.2.0")));
    }

    [Fact]
    public void PreReleaseIdentifiersOrderNumericallyAndByLength()
    {
        Assert.True(Parse("1.0.0-rc.10").IsNewerThan(Parse("1.0.0-rc.2")));
        Assert.True(Parse("1.0.0-rc.1.2").IsNewerThan(Parse("1.0.0-rc.1")));
        Assert.True(Parse("1.0.0-beta").IsNewerThan(Parse("1.0.0-1")));
    }

    [Fact]
    public void BuildMetadataTakesNoPartInOrdering()
    {
        Assert.Equal(0, Parse("1.2.0+one").CompareTo(Parse("1.2.0+two")));
    }

    [Fact]
    public void PrintsWithoutTheLeadingV()
    {
        Assert.Equal("1.2.0", Parse("v1.2.0").ToString());
        Assert.Equal("1.2.0-rc.1", Parse("v1.2.0-rc.1").ToString());
    }

    private static ReleaseVersion Parse(string text) =>
        ReleaseVersion.TryParse(text, out var v) ? v : throw new ArgumentException(text);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/TrispotQR.Core.Tests/TrispotQR.Core.Tests.csproj --filter "FullyQualifiedName~ReleaseVersionTests"`
Expected: build FAILS, `ReleaseVersion` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace TrispotQR.Core.Updates;

/// <summary>
/// A released version, and whether one is newer than another.
///
/// Exists because the obvious comparison is wrong: ordered as text, "1.10.0" comes before
/// "1.9.0". A subset of semantic versioning: three numbers and an optional pre-release suffix.
/// Build metadata is parsed and discarded, because it takes no part in ordering.
/// </summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, string? PreRelease)
    : IComparable<ReleaseVersion>
{
    public bool IsPreRelease => !string.IsNullOrEmpty(PreRelease);

    /// <summary>
    /// Reads a version from a tag. Tolerates a leading "v" and surrounding space. "1" and "1.2"
    /// read as 1.0.0 and 1.2.0. Four parts are refused rather than truncated.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.Trim();
        if (span[0] is 'v' or 'V')
        {
            span = span[1..];
        }

        var plus = span.IndexOf('+');
        if (plus >= 0)
        {
            span = span[..plus];
        }

        string? pre = null;
        var dash = span.IndexOf('-');
        if (dash >= 0)
        {
            pre = span[(dash + 1)..];
            span = span[..dash];
            if (pre.Length == 0)
            {
                return false;
            }
        }

        var parts = span.Split('.');
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new ReleaseVersion(numbers[0], numbers[1], numbers[2], pre);
        return true;
    }

    public bool IsNewerThan(ReleaseVersion other) => CompareTo(other) > 0;

    public int CompareTo(ReleaseVersion other)
    {
        if (Major != other.Major)
        {
            return Major.CompareTo(other.Major);
        }

        if (Minor != other.Minor)
        {
            return Minor.CompareTo(other.Minor);
        }

        return Patch != other.Patch
            ? Patch.CompareTo(other.Patch)
            : ComparePreRelease(PreRelease, other.PreRelease);
    }

    /// <summary>
    /// Semver pre-release ordering. A version WITH a suffix is older than the same version
    /// without one: getting that backwards offers people on 1.2.0 a downgrade to 1.2.0-rc.1.
    /// </summary>
    private static int ComparePreRelease(string? left, string? right)
    {
        var leftFinal = string.IsNullOrEmpty(left);
        var rightFinal = string.IsNullOrEmpty(right);

        if (leftFinal && rightFinal)
        {
            return 0;
        }

        if (leftFinal)
        {
            return 1;
        }

        if (rightFinal)
        {
            return -1;
        }

        var a = left!.Split('.');
        var b = right!.Split('.');

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNumeric = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var an);
            var bNumeric = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var bn);

            if (aNumeric && bNumeric)
            {
                if (an != bn)
                {
                    return an.CompareTo(bn);
                }

                continue;
            }

            // Numeric identifiers rank below alphanumeric ones.
            if (aNumeric)
            {
                return -1;
            }

            if (bNumeric)
            {
                return 1;
            }

            var text = string.CompareOrdinal(a[i], b[i]);
            if (text != 0)
            {
                return text < 0 ? -1 : 1;
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    public override string ToString() =>
        IsPreRelease ? $"{Major}.{Minor}.{Patch}-{PreRelease}" : $"{Major}.{Minor}.{Patch}";
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/TrispotQR.Core.Tests/TrispotQR.Core.Tests.csproj --filter "FullyQualifiedName~ReleaseVersionTests"`
Expected: PASS, 19 tests.

- [ ] **Step 5: Commit**

```bash
git add src/TrispotQR.Core/Updates/ReleaseVersion.cs tests/TrispotQR.Core.Tests/Updates/ReleaseVersionTests.cs
git commit -m "feat(updates): parse and order release versions"
```

---

### Task 2: Release feed parsing and the update decision

**Files:**
- Create: `src/TrispotQR.Core/Updates/ReleaseFeed.cs`
- Create: `src/TrispotQR.Core/Updates/UpdateDecision.cs`
- Test: `tests/TrispotQR.Core.Tests/Updates/ReleaseFeedTests.cs`
- Test: `tests/TrispotQR.Core.Tests/Updates/UpdateDecisionTests.cs`

**Interfaces:**
- Consumes: `ReleaseVersion` (Task 1).
- Produces:
  - `public sealed record ReleaseAsset(string Name, string Url, long Size)`
  - `public sealed record ReleaseInfo(string? Tag, string? Url, bool IsDraft = false, bool IsPreRelease = false, IReadOnlyList<ReleaseAsset>? Assets = null)`
  - `public static class ReleaseFeed { public static ReleaseInfo? Parse(string? json); }`
  - `public enum UpdateOutcome { UpToDate, Available, Unknown }`
  - `public readonly record struct UpdateVerdict(UpdateOutcome Outcome, ReleaseVersion Version, string? Url, ReleaseInfo? Release = null) { public bool IsAvailable { get; } }`
  - `public static class UpdateDecision { public static UpdateVerdict For(string? currentVersion, ReleaseInfo? latest); }`

- [ ] **Step 1: Write the failing feed tests**

```csharp
using TrispotQR.Core.Updates;

namespace TrispotQR.Tests.Updates;

public class ReleaseFeedTests
{
    private const string Release = """
        {
          "tag_name": "v1.3.0",
          "name": "Trispot QR v1.3.0",
          "html_url": "https://github.com/levinium/TrispotQR/releases/tag/v1.3.0",
          "draft": false,
          "prerelease": false,
          "assets": [
            { "name": "TrispotQR.exe", "browser_download_url": "https://example.org/TrispotQR.exe", "size": 48000000, "state": "uploaded" },
            { "name": "TrispotQR.exe.sha256", "browser_download_url": "https://example.org/TrispotQR.exe.sha256", "size": 80, "state": "uploaded" },
            { "name": "Partial.exe", "browser_download_url": "https://example.org/Partial.exe", "size": 10, "state": "starter" }
          ]
        }
        """;

    [Fact]
    public void ReadsTheFieldsThatMatter()
    {
        var info = ReleaseFeed.Parse(Release);

        Assert.NotNull(info);
        Assert.Equal("v1.3.0", info.Tag);
        Assert.Equal("https://github.com/levinium/TrispotQR/releases/tag/v1.3.0", info.Url);
        Assert.False(info.IsDraft);
        Assert.False(info.IsPreRelease);
        Assert.Equal(48000000, info.Assets!.Single(a => a.Name == "TrispotQR.exe").Size);
    }

    [Fact]
    public void SkipsAnAssetThatIsStillUploading()
    {
        // GitHub lists an asset from the moment its upload starts. Downloading one of those
        // yields a truncated file.
        Assert.DoesNotContain(ReleaseFeed.Parse(Release)!.Assets!, a => a.Name == "Partial.exe");
    }

    [Fact]
    public void ReadsTheDraftAndPreReleaseFlags()
    {
        var info = ReleaseFeed.Parse("""{ "tag_name": "v2.0.0", "draft": true, "prerelease": true }""");

        Assert.True(info!.IsDraft);
        Assert.True(info.IsPreRelease);
        Assert.Empty(info.Assets!);
    }

    [Fact]
    public void FallsBackToTheNameWhenThereIsNoTag()
    {
        Assert.Equal("1.4.0", ReleaseFeed.Parse("""{ "name": "1.4.0" }""")!.Tag);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html><body>Sign in to continue</body></html>")]
    [InlineData("[]")]
    [InlineData("""{ "message": "API rate limit exceeded" }""")]
    public void AnythingThatIsNotAReleaseReadsAsNothing(string? json)
    {
        // A captive portal answering with HTML is the usual cause, and it means exactly what a
        // failed request means.
        Assert.Null(ReleaseFeed.Parse(json));
    }
}
```

- [ ] **Step 2: Write the failing decision tests**

```csharp
using TrispotQR.Core.Updates;

namespace TrispotQR.Tests.Updates;

public class UpdateDecisionTests
{
    private static ReleaseInfo Release(string tag, bool draft = false, bool pre = false) =>
        new(tag, "https://example.org/release", draft, pre, []);

    [Fact]
    public void ANewerReleaseIsAvailableAndCarriesTheRelease()
    {
        var release = Release("v1.3.0");
        var verdict = UpdateDecision.For("1.2.0", release);

        Assert.Equal(UpdateOutcome.Available, verdict.Outcome);
        Assert.True(verdict.IsAvailable);
        Assert.Equal("1.3.0", verdict.Version.ToString());
        Assert.Same(release, verdict.Release);
    }

    [Theory]
    [InlineData("1.2.0")]
    [InlineData("1.3.0")]
    public void TheSameOrAnOlderReleaseIsUpToDate(string running)
    {
        Assert.Equal(UpdateOutcome.UpToDate, UpdateDecision.For(running, Release("v1.2.0")).Outcome);
    }

    [Fact]
    public void NoReleaseReadIsUnknown()
    {
        Assert.Equal(UpdateOutcome.Unknown, UpdateDecision.For("1.2.0", null).Outcome);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("1.0.0.1")]
    public void AnUnreadableTagIsUnknown(string tag)
    {
        Assert.Equal(UpdateOutcome.Unknown, UpdateDecision.For("1.2.0", Release(tag)).Outcome);
    }

    [Fact]
    public void AnUnreadableRunningVersionIsUnknown()
    {
        Assert.Equal(UpdateOutcome.Unknown, UpdateDecision.For("dev", Release("v9.0.0")).Outcome);
    }

    [Fact]
    public void DraftsAndPreReleasesAreNeverOffered()
    {
        Assert.False(UpdateDecision.For("1.0.0", Release("v2.0.0", draft: true)).IsAvailable);
        Assert.False(UpdateDecision.For("1.0.0", Release("v2.0.0", pre: true)).IsAvailable);

        // Unlabelled, but the tag says what it is.
        Assert.False(UpdateDecision.For("1.0.0", Release("v2.0.0-rc.1")).IsAvailable);
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/TrispotQR.Core.Tests/TrispotQR.Core.Tests.csproj --filter "FullyQualifiedName~Updates"`
Expected: build FAILS, `ReleaseFeed` and `UpdateDecision` do not exist.

- [ ] **Step 4: Implement `ReleaseFeed.cs`**

```csharp
using System.Text.Json;

namespace TrispotQR.Core.Updates;

/// <summary>A file published with a release.</summary>
public sealed record ReleaseAsset(string Name, string Url, long Size);

/// <summary>A published release, reduced to what the decision and the installer need.</summary>
public sealed record ReleaseInfo(
    string? Tag,
    string? Url,
    bool IsDraft = false,
    bool IsPreRelease = false,
    IReadOnlyList<ReleaseAsset>? Assets = null);

/// <summary>
/// Reads GitHub's "latest release" document.
///
/// Hand read rather than deserialized into a type: the response carries dozens of fields this app
/// has no interest in, and binding to them would turn an unrelated upstream change into a parse
/// failure here.
/// </summary>
public static class ReleaseFeed
{
    public static ReleaseInfo? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // The tag is the version; the name is a title someone typed.
            var tag = Text(root, "tag_name") ?? Text(root, "name");
            if (tag is null)
            {
                return null;
            }

            return new ReleaseInfo(tag, Text(root, "html_url"), Flag(root, "draft"), Flag(root, "prerelease"), Assets(root));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Only assets with a name, a link and state "uploaded". GitHub lists an asset as soon as its
    /// upload starts, so a release being published right now can advertise a partial file.
    /// </summary>
    private static IReadOnlyList<ReleaseAsset> Assets(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var found = new List<ReleaseAsset>();

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = Text(asset, "name");
            var url = Text(asset, "browser_download_url");
            var state = Text(asset, "state");

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)
                || (state is not null && state != "uploaded"))
            {
                continue;
            }

            var size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
            found.Add(new ReleaseAsset(name, url, size));
        }

        return found;
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
```

- [ ] **Step 5: Implement `UpdateDecision.cs`**

```csharp
namespace TrispotQR.Core.Updates;

public enum UpdateOutcome
{
    /// <summary>Nothing newer is published.</summary>
    UpToDate,

    /// <summary>A newer release exists and is worth mentioning.</summary>
    Available,

    /// <summary>The question could not be answered: offline, refused, unreadable.</summary>
    Unknown,
}

/// <summary>What a check concluded, and the release it is about.</summary>
public readonly record struct UpdateVerdict(
    UpdateOutcome Outcome,
    ReleaseVersion Version,
    string? Url,
    ReleaseInfo? Release = null)
{
    public bool IsAvailable => Outcome == UpdateOutcome.Available;
}

/// <summary>
/// Whether a published release is one to tell the user about.
///
/// Every uncertain case resolves to Unknown rather than to either confident answer. A prompt
/// that fires on a version it could not read sends people to download something they may already
/// have; a false "up to date" is quieter but still untrue. Drafts and pre-releases are never
/// offered.
/// </summary>
public static class UpdateDecision
{
    public static UpdateVerdict For(string? currentVersion, ReleaseInfo? latest)
    {
        if (latest is null)
        {
            return new UpdateVerdict(UpdateOutcome.Unknown, default, null);
        }

        if (latest.IsDraft || latest.IsPreRelease)
        {
            return new UpdateVerdict(UpdateOutcome.UpToDate, default, null);
        }

        if (!ReleaseVersion.TryParse(latest.Tag, out var published))
        {
            return new UpdateVerdict(UpdateOutcome.Unknown, default, null);
        }

        if (published.IsPreRelease)
        {
            return new UpdateVerdict(UpdateOutcome.UpToDate, default, null);
        }

        if (!ReleaseVersion.TryParse(currentVersion, out var running))
        {
            return new UpdateVerdict(UpdateOutcome.Unknown, published, latest.Url);
        }

        return published.IsNewerThan(running)
            ? new UpdateVerdict(UpdateOutcome.Available, published, latest.Url, latest)
            : new UpdateVerdict(UpdateOutcome.UpToDate, published, latest.Url, latest);
    }
}
```

- [ ] **Step 6: Run to verify they pass**

Run: `dotnet test tests/TrispotQR.Core.Tests/TrispotQR.Core.Tests.csproj --filter "FullyQualifiedName~Updates"`
Expected: PASS, all Task 1 and Task 2 tests.

- [ ] **Step 7: Commit**

```bash
git add src/TrispotQR.Core/Updates tests/TrispotQR.Core.Tests/Updates
git commit -m "feat(updates): read the release feed and decide whether to offer it"
```

---

### Task 3: Assets, file plan and schedule

**Files:**
- Create: `src/TrispotQR.Core/Updates/UpdateAssets.cs`
- Create: `src/TrispotQR.Core/Updates/UpdatePlan.cs`
- Create: `src/TrispotQR.Core/Updates/UpdateSchedule.cs`
- Test: `tests/TrispotQR.Core.Tests/Updates/UpdateAssetsTests.cs`
- Test: `tests/TrispotQR.Core.Tests/Updates/UpdatePlanAndScheduleTests.cs`

**Interfaces:**
- Consumes: `ReleaseAsset` (Task 2).
- Produces:
  - `UpdateAssets`: `const string ExeName = "TrispotQR.exe"`, `const string ChecksumName = "TrispotQR.exe.sha256"`, `const long MostBytes = 256L * 1024 * 1024`, `ReleaseAsset? Executable(IReadOnlyList<ReleaseAsset>?)`, `ReleaseAsset? Checksum(IReadOnlyList<ReleaseAsset>?)`, `bool TryReadChecksum(string?, out string hash)`, `string Format(byte[] hash)`, `bool Matches(string? expected, string? actual)`, `bool IsAllowedUrl(string? url)`.
  - `public sealed record UpdatePlan(string Current, string Staged, string Backup)` with `const string StagedSuffix = ".new"`, `const string BackupSuffix = ".old"`, `string Directory`, `static UpdatePlan For(string currentExePath)`, `IEnumerable<string> Leftovers()`.
  - `UpdateSchedule`: `static readonly TimeSpan Interval` (1 day), `static bool IsDue(DateTimeOffset? lastCheckUtc, DateTimeOffset nowUtc)`.

- [ ] **Step 1: Write the failing asset tests**

```csharp
using TrispotQR.Core.Updates;

namespace TrispotQR.Tests.Updates;

public class UpdateAssetsTests
{
    private static readonly ReleaseAsset Exe = new("TrispotQR.exe", "https://example.org/TrispotQR.exe", 48_000_000);
    private static readonly ReleaseAsset Sum = new("TrispotQR.exe.sha256", "https://example.org/TrispotQR.exe.sha256", 80);
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void PicksTheExecutableByItsWholeName()
    {
        // The checksum file's name CONTAINS the exe's. Matching on a substring would install 80
        // bytes of text over the app.
        Assert.Same(Exe, UpdateAssets.Executable([Sum, Exe]));
        Assert.Same(Sum, UpdateAssets.Checksum([Exe, Sum]));
    }

    [Fact]
    public void AReleaseWithoutTheFilesHasNothingToOffer()
    {
        Assert.Null(UpdateAssets.Executable([Sum]));
        Assert.Null(UpdateAssets.Checksum([Exe]));
        Assert.Null(UpdateAssets.Executable(null));
    }

    [Theory]
    [InlineData(Hash + "  TrispotQR.exe")]
    [InlineData(Hash + " *TrispotQR.exe\r\n")]
    [InlineData("\uFEFF" + Hash + "\n")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF  TrispotQR.exe")]
    public void ReadsTheChecksumInTheShapesItArrivesIn(string text)
    {
        Assert.True(UpdateAssets.TryReadChecksum(text, out var hash));
        Assert.Equal(Hash, hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a hash")]
    [InlineData("0123456789abcdef  TrispotQR.exe")]
    [InlineData("zz23456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void RefusesAChecksumThatIsNotOne(string? text)
    {
        Assert.False(UpdateAssets.TryReadChecksum(text, out _));
    }

    [Fact]
    public void ComparesHashesWithoutCaringAboutCase()
    {
        // PowerShell's Get-FileHash writes upper case; an ordinal comparison would reject every
        // genuine update while looking exactly like a tampered one.
        Assert.True(UpdateAssets.Matches(Hash.ToUpperInvariant(), Hash));
        Assert.False(UpdateAssets.Matches(Hash, Hash.Replace('0', '1')));
        Assert.False(UpdateAssets.Matches(null, Hash));
        Assert.False(UpdateAssets.Matches(Hash, ""));
    }

    [Fact]
    public void FormatsAHashTheWayTheChecksumFileWritesIt()
    {
        Assert.Equal("00ff10", UpdateAssets.Format([0x00, 0xFF, 0x10]));
    }

    [Theory]
    [InlineData("https://api.github.com/repos/levinium/TrispotQR/releases/latest", true)]
    [InlineData("http://localhost:8123/release.json", true)]
    [InlineData("http://127.0.0.1:8123/TrispotQR.exe", true)]
    [InlineData("http://[::1]:8123/TrispotQR.exe", true)]
    [InlineData("http://example.org/TrispotQR.exe", false)]
    [InlineData("ftp://example.org/TrispotQR.exe", false)]
    [InlineData("file:///C:/TrispotQR.exe", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void OnlyHttpsOrALoopbackAddressIsAllowed(string? url, bool allowed)
    {
        Assert.Equal(allowed, UpdateAssets.IsAllowedUrl(url));
    }
}
```

- [ ] **Step 2: Write the failing plan and schedule tests**

```csharp
using TrispotQR.Core.Updates;

namespace TrispotQR.Tests.Updates;

public class UpdatePlanAndScheduleTests
{
    [Fact]
    public void ThePlanKeepsEverythingBesideTheRunningExe()
    {
        // Beside, not in temp: a rename within one volume is atomic, and a copy between volumes
        // can fail halfway with the old exe already moved aside.
        var exe = Path.Combine(Path.GetTempPath(), "apps", "TrispotQR.exe");
        var plan = UpdatePlan.For(exe);

        Assert.Equal(exe, plan.Current);
        Assert.Equal(exe + ".new", plan.Staged);
        Assert.Equal(exe + ".old", plan.Backup);
        Assert.Equal(Path.GetDirectoryName(exe), plan.Directory);
        Assert.Equal([plan.Backup, plan.Staged], plan.Leftovers());
    }

    [Fact]
    public void APlanNeedsAPath()
    {
        Assert.ThrowsAny<ArgumentException>(() => UpdatePlan.For(" "));
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NeverCheckedIsDue() => Assert.True(UpdateSchedule.IsDue(null, Now));

    [Fact]
    public void LessThanADayAgoIsNotDue() => Assert.False(UpdateSchedule.IsDue(Now.AddHours(-23), Now));

    [Fact]
    public void ADayAgoIsDue() => Assert.True(UpdateSchedule.IsDue(Now.AddDays(-1), Now));

    [Fact]
    public void ACheckTimeInTheFutureIsDue()
    {
        // Left behind by a clock correction. Treated as "checked recently" it would stop the
        // check forever, silently.
        Assert.True(UpdateSchedule.IsDue(Now.AddDays(3), Now));
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/TrispotQR.Core.Tests/TrispotQR.Core.Tests.csproj --filter "FullyQualifiedName~Updates"`
Expected: build FAILS, the three types do not exist.

- [ ] **Step 4: Implement `UpdateAssets.cs`**

```csharp
using System.Globalization;

namespace TrispotQR.Core.Updates;

/// <summary>
/// Which of a release's files to fetch, whether what arrived is what was published, and which
/// addresses may be fetched at all. All string handling, which is where self-updates actually go
/// wrong, so all of it is tested without a network.
/// </summary>
public static class UpdateAssets
{
    public const string ExeName = "TrispotQR.exe";

    public const string ChecksumName = "TrispotQR.exe.sha256";

    /// <summary>
    /// A guard, not a tight bound: the exe is under 50 MB. Something answering the download with
    /// an endless stream fills the disk no further than this.
    /// </summary>
    public const long MostBytes = 256L * 1024 * 1024;

    /// <summary>Matched on the whole name: the checksum file's name contains the exe's.</summary>
    public static ReleaseAsset? Executable(IReadOnlyList<ReleaseAsset>? assets) => Named(assets, ExeName);

    public static ReleaseAsset? Checksum(IReadOnlyList<ReleaseAsset>? assets) => Named(assets, ChecksumName);

    /// <summary>
    /// Reads the hash from a sha256sum-format file: the hash, whitespace, the file name. Only
    /// the hash is taken, so CRLF endings, a BOM or a "*" binary marker still read.
    /// </summary>
    public static bool TryReadChecksum(string? text, out string hash)
    {
        hash = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var first = text.TrimStart('\uFEFF')
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        var token = first?.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        if (token is null || token.Length != 64 || !token.All(char.IsAsciiHexDigit))
        {
            return false;
        }

        hash = token.ToLowerInvariant();
        return true;
    }

    public static string Format(byte[] hash) =>
        string.Concat(hash.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));

    /// <summary>Case insensitive: the two sides are written by different tools.</summary>
    public static bool Matches(string? expected, string? actual) =>
        !string.IsNullOrWhiteSpace(expected)
        && !string.IsNullOrWhiteSpace(actual)
        && string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// HTTPS, or plain HTTP to this machine only. The loopback exception exists so the end to
    /// end test can serve a release locally; a real address over plain HTTP could be swapped by
    /// anyone on the network path, checksum and all.
    /// </summary>
    public static bool IsAllowedUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }

    private static ReleaseAsset? Named(IReadOnlyList<ReleaseAsset>? assets, string name) =>
        assets?.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 5: Implement `UpdatePlan.cs`**

```csharp
namespace TrispotQR.Core.Updates;

/// <summary>
/// The three paths an in-place update moves between.
///
/// Windows will not let a running exe be overwritten or deleted, but it will let one be renamed.
/// So: rename the running exe to its backup name, rename the staged download into the name it
/// vacated, start it, and let the new process delete the backup. Fixed names, so a crash between
/// any two steps leaves files the next launch recognizes and removes.
/// </summary>
public sealed record UpdatePlan(string Current, string Staged, string Backup)
{
    public const string StagedSuffix = ".new";

    public const string BackupSuffix = ".old";

    public string Directory => Path.GetDirectoryName(Current) ?? string.Empty;

    public static UpdatePlan For(string currentExePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentExePath);
        return new UpdatePlan(currentExePath, currentExePath + StagedSuffix, currentExePath + BackupSuffix);
    }

    /// <summary>
    /// Safe to delete unconditionally at startup: an exe is running under the real name, so the
    /// backup is superseded and any staged download was never installed.
    /// </summary>
    public IEnumerable<string> Leftovers()
    {
        yield return Backup;
        yield return Staged;
    }
}
```

- [ ] **Step 6: Implement `UpdateSchedule.cs`**

```csharp
namespace TrispotQR.Core.Updates;

/// <summary>How often the app is allowed to go and look.</summary>
public static class UpdateSchedule
{
    /// <summary>
    /// Once a day. Releases arrive weeks apart, so more often learns the same answer at someone
    /// else's expense; less often lets a fix sit unnoticed.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>
    /// A stored time in the future counts as due. It should be impossible, but a clock correction
    /// leaves exactly that, and the naive comparison would never check again.
    /// </summary>
    public static bool IsDue(DateTimeOffset? lastCheckUtc, DateTimeOffset nowUtc)
    {
        if (lastCheckUtc is not { } last || last > nowUtc)
        {
            return true;
        }

        return nowUtc - last >= Interval;
    }
}
```

- [ ] **Step 7: Run to verify they pass**

Run: `dotnet test tests/TrispotQR.Core.Tests/TrispotQR.Core.Tests.csproj --filter "FullyQualifiedName~Updates"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/TrispotQR.Core/Updates tests/TrispotQR.Core.Tests/Updates
git commit -m "feat(updates): choose and verify release files, plan the swap, schedule the check"
```

---

### Task 4: Settings for the check

**Files:**
- Modify: `src/TrispotQR.Core/Presets/AppSettings.cs` (inside `record AppSettings`, after `RecentColors`)
- Modify: `tests/TrispotQR.Core.Tests/PresetStoreTests.cs` (`Settings_RoundTripThroughDisk`, around line 346)

**Interfaces:**
- Produces: `public bool CheckForUpdates { get; init; } = true;` and `public DateTimeOffset? LastUpdateCheckUtc { get; init; }` on `AppSettings`.

- [ ] **Step 1: Extend the round-trip test first**

In `Settings_RoundTripThroughDisk`, add to the object initializer:

```csharp
            CheckForUpdates = false,
            LastUpdateCheckUtc = new DateTimeOffset(2026, 9, 14, 9, 30, 0, TimeSpan.Zero),
```

and append these assertions after `Assert.Equal(settings.RecentColors, reloaded.RecentColors);`:

```csharp
        Assert.Equal(settings.CheckForUpdates, reloaded.CheckForUpdates);
        Assert.Equal(settings.LastUpdateCheckUtc, reloaded.LastUpdateCheckUtc);
```

Add a new test below it:

```csharp
    [Fact]
    public void Settings_ASettingsFileFromBeforeUpdatesStillChecksForThem()
    {
        // Every existing settings.json predates these members. Missing must mean the default,
        // which is on, or everyone upgrading from 1.1.0 would silently never hear of 1.3.0.
        File.WriteAllText(Path.Combine(_directory, "settings.json"), """{ "WindowWidth": 1000 }""");

        var loaded = new AppSettingsStore(_directory).Load();

        Assert.True(loaded.CheckForUpdates);
        Assert.Null(loaded.LastUpdateCheckUtc);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/TrispotQR.Core.Tests/TrispotQR.Core.Tests.csproj --filter "FullyQualifiedName~Settings_"`
Expected: build FAILS, `CheckForUpdates` does not exist.

- [ ] **Step 3: Add the members**

```csharp
    /// <summary>
    /// Whether the app looks for a newer release at launch, at most once a day. A preference, on
    /// by default. Asking from the gear menu works either way: this governs what the app does
    /// unasked.
    /// </summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>When a check last completed with an answer. Session state.</summary>
    public DateTimeOffset? LastUpdateCheckUtc { get; init; }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/TrispotQR.Core.Tests/TrispotQR.Core.Tests.csproj`
Expected: PASS, whole Core suite.

- [ ] **Step 5: Commit**

```bash
git add src/TrispotQR.Core/Presets/AppSettings.cs tests/TrispotQR.Core.Tests/PresetStoreTests.cs
git commit -m "feat(updates): remember whether to check and when it last did"
```

---

### Task 5: The updater contract and the notice in the view model

**Files:**
- Create: `src/TrispotQR.ViewModels/IUpdater.cs`
- Create: `src/TrispotQR.ViewModels/MainViewModel.Updates.cs`
- Modify: `src/TrispotQR.ViewModels/MainViewModel.cs` (class declaration line 25, constructor line 56 and its last lines, `OpenSettings` around line 601)
- Modify: `tests/TrispotQR.ViewModels.Tests/MainViewModelTests.cs` (class declaration line 13, `FakeDialogService` around line 902)
- Create: `tests/TrispotQR.ViewModels.Tests/MainViewModelTests.Updates.cs`

**Interfaces:**
- Consumes: `UpdateVerdict`, `UpdateOutcome`, `ReleaseInfo`, `UpdateSchedule` (Tasks 2 and 3); `AppSettings.CheckForUpdates`, `AppSettings.LastUpdateCheckUtc` (Task 4).
- Produces:
  - `public enum InstallOutcome { Staged, NothingToInstall, NotSupported, NotWritable, DownloadFailed, VerificationFailed, Canceled }`
  - `public readonly record struct InstallResult(InstallOutcome Outcome, string? Detail = null) { public bool IsStaged { get; } }`
  - `public interface IUpdater { string CurrentVersion { get; } Task<UpdateVerdict> CheckAsync(CancellationToken ct = default); InstallResult CanInstall(); Task<InstallResult> StageAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct = default); bool Apply(bool restart); void OpenReleasePage(string? url); }`
  - `public enum UpdateNoticeState { None, Available, Downloading, Ready, Failed }`
  - `MainViewModel` constructor gains trailing optional parameters `IUpdater? updater = null, Func<DateTimeOffset>? clock = null`.
  - `MainViewModel` members: `UpdateNoticeState UpdateState`, `bool IsUpdateNoticeVisible`, `bool IsUpdateDownloading`, `bool CanDismissUpdate`, `bool CanCheckForUpdates`, `double UpdateProgress`, `string UpdateHeadline`, `string UpdateDetail`, `string UpdatePrimaryLabel`, `string UpdateDismissLabel`, `RelayCommand UpdatePrimaryCommand`, `RelayCommand WhatsNewCommand`, `RelayCommand DismissUpdateCommand`, `RelayCommand CheckForUpdatesCommand`, `Task CheckForUpdatesAtStartupAsync()`, `Task CheckForUpdatesNowAsync()`, `Task StageUpdateAsync()`, `void RestartToUpdate()`, `void ApplyStagedUpdateOnExit()`, `event EventHandler? RestartRequested`.

- [ ] **Step 1: Make room for the partial files and the test seams**

In `MainViewModel.cs` line 25 change `public sealed class MainViewModel : ObservableObject` to `public sealed partial class MainViewModel : ObservableObject`.

In `MainViewModelTests.cs` line 13 change `public class MainViewModelTests : IDisposable` to `public partial class MainViewModelTests : IDisposable`.

In `MainViewModelTests.cs`, inside `FakeDialogService`, replace

```csharp
        public void ShowInformation(string title, string message)
        {
        }

        public AppSettings? EditSettings(AppSettings current) => null;
```

with

```csharp
        public List<string> Informations { get; } = [];

        public void ShowInformation(string title, string message) => Informations.Add(message);

        /// <summary>What the settings window hands back. Null means the user cancelled.</summary>
        public AppSettings? NextSettings { get; set; }

        public AppSettings? EditSettings(AppSettings current) => NextSettings;
```

Run: `dotnet test tests/TrispotQR.ViewModels.Tests/TrispotQR.ViewModels.Tests.csproj`
Expected: PASS, 72 tests (no behavior changed yet).

- [ ] **Step 2: Create the contract, `IUpdater.cs`**

```csharp
using TrispotQR.Core.Updates;

namespace TrispotQR.ViewModels;

/// <summary>How far an install got, and why it stopped there.</summary>
public enum InstallOutcome
{
    /// <summary>Downloaded, verified and staged beside the app. Or, from CanInstall, possible.</summary>
    Staged,

    /// <summary>The release has no TrispotQR.exe attached.</summary>
    NothingToInstall,

    /// <summary>This copy cannot replace itself: a development build, or not Windows.</summary>
    NotSupported,

    /// <summary>The app's folder cannot be written without administrator rights.</summary>
    NotWritable,

    DownloadFailed,

    /// <summary>What arrived is not what was published.</summary>
    VerificationFailed,

    Canceled,
}

/// <param name="Detail">A sentence for the user, or null when there is nothing to add.</param>
public readonly record struct InstallResult(InstallOutcome Outcome, string? Detail = null)
{
    public bool IsStaged => Outcome == InstallOutcome.Staged;
}

/// <summary>
/// Everything the view model needs to find and install a newer release. Behind an interface so
/// the notice's behavior is tested without a network, and optional so the WPF app, which is
/// being retired, passes nothing and simply has no updates.
/// </summary>
public interface IUpdater
{
    /// <summary>The running build's version, for "You have the latest version".</summary>
    string CurrentVersion { get; }

    /// <summary>Asks once. Never throws: every failure is an Unknown verdict.</summary>
    Task<UpdateVerdict> CheckAsync(CancellationToken ct = default);

    /// <summary>Whether this copy could install in place, asked before the button is offered.</summary>
    InstallResult CanInstall();

    /// <summary>
    /// Downloads and verifies the release's exe beside the app. Changes nothing installed.
    /// Progress is reported on the UI thread, because the view model applies it straight to a
    /// bound property; an implementation that downloads on the thread pool must marshal it.
    /// </summary>
    Task<InstallResult> StageAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct = default);

    /// <summary>
    /// Swaps the staged exe into place. With <paramref name="restart"/>, also starts it. Returns
    /// true when installed; false means nothing was changed.
    /// </summary>
    bool Apply(bool restart);

    /// <summary>Opens a release page in the browser; the project's page when the url is null.</summary>
    void OpenReleasePage(string? url);
}
```

- [ ] **Step 3: Write the failing view model tests, `MainViewModelTests.Updates.cs`**

```csharp
using TrispotQR.Core.Presets;
using TrispotQR.Core.Updates;
using TrispotQR.ViewModels;

namespace TrispotQR.ViewModels.Tests;

public partial class MainViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private const string ReleaseUrl = "https://example.org/releases/v1.3.0";

    private static UpdateVerdict Newer() =>
        UpdateDecision.For("1.2.0", new ReleaseInfo("v1.3.0", ReleaseUrl, Assets: []));

    private static UpdateVerdict Current() =>
        UpdateDecision.For("1.3.0", new ReleaseInfo("v1.3.0", ReleaseUrl, Assets: []));

    private MainViewModel CreateWithUpdater(FakeUpdater updater, AppSettings? settings = null)
    {
        if (settings is not null)
        {
            new AppSettingsStore(_directory).Save(settings);
        }

        return new MainViewModel(
            _dialogs, new FakeUiTimer(), new FakeImageClipboard(), new PresetStore(_directory),
            new AppSettingsStore(_directory), updater, () => Now);
    }

    [Fact]
    public async Task Updates_NothingIsShownWhenTheBuildIsCurrent()
    {
        // Which is nearly always. A line permanently on screen saying there is no news is worse
        // than no line.
        var vm = CreateWithUpdater(new FakeUpdater { Verdict = Current() });

        await vm.CheckForUpdatesAtStartupAsync();

        Assert.False(vm.IsUpdateNoticeVisible);
        Assert.Equal(UpdateNoticeState.None, vm.UpdateState);
    }

    [Fact]
    public async Task Updates_AFoundReleaseIsNamedInTheNotice()
    {
        var vm = CreateWithUpdater(new FakeUpdater { Verdict = Newer() });

        await vm.CheckForUpdatesAtStartupAsync();

        Assert.True(vm.IsUpdateNoticeVisible);
        Assert.Contains("1.3.0", vm.UpdateHeadline);
        Assert.Equal("Update now", vm.UpdatePrimaryLabel);
        Assert.True(vm.CanDismissUpdate);
    }

    [Fact]
    public async Task Updates_AnUnansweredCheckShowsNothingAndIsTriedAgainNextLaunch()
    {
        var updater = new FakeUpdater { Verdict = UpdateDecision.For("1.2.0", null) };
        var vm = CreateWithUpdater(updater);

        await vm.CheckForUpdatesAtStartupAsync();

        Assert.False(vm.IsUpdateNoticeVisible);

        // Recording an unanswered check would silence the next day's attempt for no reason.
        Assert.Null(new AppSettingsStore(_directory).Load().LastUpdateCheckUtc);
    }

    [Fact]
    public async Task Updates_WithTheSettingOffNothingIsAsked()
    {
        var updater = new FakeUpdater { Verdict = Newer() };
        var vm = CreateWithUpdater(updater, AppSettings.Default with { CheckForUpdates = false });

        await vm.CheckForUpdatesAtStartupAsync();

        Assert.Equal(0, updater.Checks);
        Assert.False(vm.IsUpdateNoticeVisible);
    }

    [Fact]
    public async Task Updates_ACheckWithinTheLastDayIsNotRepeated()
    {
        var updater = new FakeUpdater { Verdict = Newer() };
        var vm = CreateWithUpdater(updater, AppSettings.Default with { LastUpdateCheckUtc = Now.AddHours(-2) });

        await vm.CheckForUpdatesAtStartupAsync();

        Assert.Equal(0, updater.Checks);
    }

    [Fact]
    public async Task Updates_ADueCheckRunsAndRecordsWhenItRan()
    {
        var updater = new FakeUpdater { Verdict = Current() };
        var vm = CreateWithUpdater(updater, AppSettings.Default with { LastUpdateCheckUtc = Now.AddDays(-2) });

        await vm.CheckForUpdatesAtStartupAsync();

        Assert.Equal(1, updater.Checks);
        Assert.Equal(Now, new AppSettingsStore(_directory).Load().LastUpdateCheckUtc);
    }

    [Fact]
    public async Task Updates_AskingFromTheMenuWorksWithTheSettingOffAndSaysWhatItFound()
    {
        // The setting governs what the app does unasked. A person asking is a different thing.
        var updater = new FakeUpdater { Verdict = Current() };
        var vm = CreateWithUpdater(updater, AppSettings.Default with { CheckForUpdates = false });

        await vm.CheckForUpdatesNowAsync();

        Assert.Equal(1, updater.Checks);
        Assert.Contains(_dialogs.Informations, m => m.Contains("latest version") && m.Contains("1.3.0"));
    }

    [Fact]
    public async Task Updates_AnUnansweredMenuCheckSaysSoRatherThanClaimingUpToDate()
    {
        var vm = CreateWithUpdater(new FakeUpdater { Verdict = UpdateDecision.For("1.2.0", null) });

        await vm.CheckForUpdatesNowAsync();

        Assert.Contains(_dialogs.Informations, m => m.Contains("Could not check"));
    }

    [Fact]
    public async Task Updates_WhereItCannotInstallTheButtonOpensTheReleasePageInstead()
    {
        var updater = new FakeUpdater
        {
            Verdict = Newer(),
            Readiness = new InstallResult(InstallOutcome.NotWritable, "Trispot QR is installed where it cannot update itself."),
        };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();

        Assert.Equal("Download", vm.UpdatePrimaryLabel);
        Assert.Contains("cannot update itself", vm.UpdateDetail);

        vm.UpdatePrimaryCommand.Execute(null);

        Assert.Equal([ReleaseUrl], updater.OpenedUrls);
        Assert.Equal(0, updater.Stages);
    }

    [Fact]
    public async Task Updates_UpdateNowDownloadsReportsProgressAndBecomesReady()
    {
        var updater = new FakeUpdater { Verdict = Newer(), ProgressToReport = 0.42 };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();

        await vm.StageUpdateAsync();

        Assert.Equal(1, updater.Stages);
        Assert.Equal(0.42, vm.UpdateProgress);
        Assert.Equal(UpdateNoticeState.Ready, vm.UpdateState);
        Assert.Equal("Restart now", vm.UpdatePrimaryLabel);
        Assert.False(vm.CanDismissUpdate);
        Assert.Contains("close", vm.UpdateDetail);
    }

    [Fact]
    public async Task Updates_AFailedDownloadSaysWhyAndInstallsNothing()
    {
        var updater = new FakeUpdater
        {
            Verdict = Newer(),
            StageResult = new InstallResult(InstallOutcome.VerificationFailed, "The download did not match the published checksum, so it was discarded."),
        };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();

        await vm.StageUpdateAsync();

        Assert.Equal(UpdateNoticeState.Failed, vm.UpdateState);
        Assert.Contains("checksum", vm.UpdateDetail);
        Assert.Equal("Try again", vm.UpdatePrimaryLabel);

        vm.ApplyStagedUpdateOnExit();
        Assert.Empty(updater.Applies);
    }

    [Fact]
    public async Task Updates_RestartNowInstallsAndAsksTheWindowToClose()
    {
        var updater = new FakeUpdater { Verdict = Newer() };
        var vm = CreateWithUpdater(updater);
        var asked = 0;
        vm.RestartRequested += (_, _) => asked++;
        await vm.CheckForUpdatesAtStartupAsync();
        await vm.StageUpdateAsync();

        vm.RestartToUpdate();

        Assert.Equal([true], updater.Applies);
        Assert.Equal(1, asked);

        // Installed already, so closing must not try a second time.
        vm.ApplyStagedUpdateOnExit();
        Assert.Equal([true], updater.Applies);
    }

    [Fact]
    public async Task Updates_ARestartThatCannotInstallStaysOpenAndSaysSo()
    {
        var updater = new FakeUpdater { Verdict = Newer(), ApplyResult = false };
        var vm = CreateWithUpdater(updater);
        var asked = 0;
        vm.RestartRequested += (_, _) => asked++;
        await vm.CheckForUpdatesAtStartupAsync();
        await vm.StageUpdateAsync();

        vm.RestartToUpdate();

        Assert.Equal(0, asked);
        Assert.Equal(UpdateNoticeState.Failed, vm.UpdateState);
    }

    [Fact]
    public async Task Updates_AReadyUpdateInstallsWhenTheAppClosesWithoutRestarting()
    {
        // Nobody should lose what they are typing to get the new version.
        var updater = new FakeUpdater { Verdict = Newer() };
        var vm = CreateWithUpdater(updater);
        await vm.CheckForUpdatesAtStartupAsync();
        await vm.StageUpdateAsync();

        vm.ApplyStagedUpdateOnExit();

        Assert.Equal([false], updater.Applies);
    }

    [Fact]
    public async Task Updates_LaterHidesTheNotice()
    {
        var vm = CreateWithUpdater(new FakeUpdater { Verdict = Newer() });
        await vm.CheckForUpdatesAtStartupAsync();

        vm.DismissUpdateCommand.Execute(null);

        Assert.False(vm.IsUpdateNoticeVisible);
    }

    [Fact]
    public async Task Updates_TurningTheSettingOffClearsTheNotice()
    {
        // Otherwise the notice stays after being told to stop looking, the opposite of what the
        // switch says it does.
        var vm = CreateWithUpdater(new FakeUpdater { Verdict = Newer() });
        await vm.CheckForUpdatesAtStartupAsync();

        _dialogs.NextSettings = AppSettings.Default with { CheckForUpdates = false };
        vm.OpenSettings();

        Assert.False(vm.IsUpdateNoticeVisible);
    }

    [Fact]
    public async Task Updates_WithoutAnUpdaterThereIsNothingToCheck()
    {
        var vm = Create();

        await vm.CheckForUpdatesAtStartupAsync();
        await vm.CheckForUpdatesNowAsync();

        Assert.False(vm.CanCheckForUpdates);
        Assert.False(vm.IsUpdateNoticeVisible);
    }

    private sealed class FakeUpdater : IUpdater
    {
        public UpdateVerdict Verdict { get; init; }

        public InstallResult Readiness { get; init; } = new(InstallOutcome.Staged);

        public InstallResult StageResult { get; init; } = new(InstallOutcome.Staged);

        public bool ApplyResult { get; init; } = true;

        public double? ProgressToReport { get; init; }

        public int Checks { get; private set; }

        public int Stages { get; private set; }

        public List<bool> Applies { get; } = [];

        public List<string?> OpenedUrls { get; } = [];

        public string CurrentVersion => "1.3.0";

        public Task<UpdateVerdict> CheckAsync(CancellationToken ct = default)
        {
            Checks++;
            return Task.FromResult(Verdict);
        }

        public InstallResult CanInstall() => Readiness;

        public Task<InstallResult> StageAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct = default)
        {
            Stages++;
            if (ProgressToReport is { } p)
            {
                progress?.Report(p);
            }

            return Task.FromResult(StageResult);
        }

        public bool Apply(bool restart)
        {
            Applies.Add(restart);
            return ApplyResult;
        }

        public void OpenReleasePage(string? url) => OpenedUrls.Add(url);
    }
}
```

- [ ] **Step 4: Run to verify they fail**

Run: `dotnet test tests/TrispotQR.ViewModels.Tests/TrispotQR.ViewModels.Tests.csproj`
Expected: build FAILS, the constructor has no `updater` parameter and the update members do not exist.

- [ ] **Step 5: Wire the constructor and `OpenSettings` in `MainViewModel.cs`**

Change the constructor signature to:

```csharp
    public MainViewModel(IDialogService dialogs, IUiTimer timer, IImageClipboard clipboard, PresetStore? presets = null, AppSettingsStore? settingsStore = null, IUpdater? updater = null, Func<DateTimeOffset>? clock = null)
```

Immediately before the constructor's closing `if (_presets.LoadWarning is { } warning)` block, add:

```csharp
        InitializeUpdates(updater, clock);
```

In `OpenSettings`, directly after `_settings = updated;`, add:

```csharp
        ClearUpdateNoticeIfChecksWereTurnedOff();
```

- [ ] **Step 6: Implement `MainViewModel.Updates.cs`**

```csharp
using TrispotQR.Core.Updates;

namespace TrispotQR.ViewModels;

/// <summary>What the update notice is showing.</summary>
public enum UpdateNoticeState
{
    None,
    Available,
    Downloading,
    Ready,
    Failed,
}

/// <summary>
/// The update notice: whether to look, what was found, and moving from "available" through
/// "downloading" to "ready". Everything here is idle unless an <see cref="IUpdater"/> was
/// supplied, which only the Avalonia app does.
/// </summary>
public sealed partial class MainViewModel
{
    private IUpdater? _updater;
    private Func<DateTimeOffset> _clock = () => DateTimeOffset.UtcNow;
    private UpdateNoticeState _updateState;
    private UpdateVerdict _verdict;
    private bool _canInstallInPlace;
    private string? _installBlockedReason;
    private string? _updateFailure;
    private double _updateProgress;
    private CancellationTokenSource? _download;

    /// <summary>Asked for by the window so it can close normally and save the session.</summary>
    public event EventHandler? RestartRequested;

    public RelayCommand UpdatePrimaryCommand { get; private set; } = null!;

    public RelayCommand WhatsNewCommand { get; private set; } = null!;

    public RelayCommand DismissUpdateCommand { get; private set; } = null!;

    public RelayCommand CheckForUpdatesCommand { get; private set; } = null!;

    public bool CanCheckForUpdates => _updater is not null;

    public UpdateNoticeState UpdateState
    {
        get => _updateState;
        private set
        {
            if (!SetField(ref _updateState, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsUpdateNoticeVisible));
            OnPropertyChanged(nameof(IsUpdateDownloading));
            OnPropertyChanged(nameof(CanDismissUpdate));
            RaiseUpdateText();
            UpdatePrimaryCommand.RaiseCanExecuteChanged();
            CheckForUpdatesCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Only when there is something to say. Nothing is ever shown to report no news.</summary>
    public bool IsUpdateNoticeVisible => UpdateState != UpdateNoticeState.None;

    public bool IsUpdateDownloading => UpdateState == UpdateNoticeState.Downloading;

    /// <summary>
    /// Later while available or failed, Cancel while downloading. A ready update has no Later:
    /// it installs itself when the app closes anyway.
    /// </summary>
    public bool CanDismissUpdate => UpdateState is UpdateNoticeState.Available or UpdateNoticeState.Failed or UpdateNoticeState.Downloading;

    public double UpdateProgress
    {
        get => _updateProgress;
        private set
        {
            if (SetField(ref _updateProgress, value))
            {
                OnPropertyChanged(nameof(UpdateHeadline));
            }
        }
    }

    public string UpdateHeadline => UpdateState switch
    {
        UpdateNoticeState.Available => $"Trispot QR {_verdict.Version} is available.",
        UpdateNoticeState.Downloading => $"Downloading Trispot QR {_verdict.Version} ({UpdateProgress * 100:0}%)",
        UpdateNoticeState.Ready => $"Trispot QR {_verdict.Version} is ready.",
        UpdateNoticeState.Failed => $"Trispot QR {_verdict.Version} could not be installed.",
        _ => string.Empty,
    };

    public string UpdateDetail => UpdateState switch
    {
        UpdateNoticeState.Available when !_canInstallInPlace => _installBlockedReason ?? string.Empty,
        UpdateNoticeState.Ready => "Restart now, or it installs when you close Trispot QR. Restarting clears what is typed in the box; saved styles and settings carry over.",
        UpdateNoticeState.Failed => _updateFailure ?? string.Empty,
        _ => string.Empty,
    };

    public string UpdatePrimaryLabel => UpdateState switch
    {
        UpdateNoticeState.Available => _canInstallInPlace ? "Update now" : "Download",
        UpdateNoticeState.Ready => "Restart now",
        UpdateNoticeState.Failed => _canInstallInPlace ? "Try again" : "Download",
        _ => string.Empty,
    };

    /// <summary>The same button stops a download in progress, and says so.</summary>
    public string UpdateDismissLabel => UpdateState == UpdateNoticeState.Downloading ? "Cancel" : "Later";

    private void InitializeUpdates(IUpdater? updater, Func<DateTimeOffset>? clock)
    {
        _updater = updater;
        _clock = clock ?? _clock;

        UpdatePrimaryCommand = new RelayCommand(OnUpdatePrimary,
            () => UpdateState is UpdateNoticeState.Available or UpdateNoticeState.Ready or UpdateNoticeState.Failed);
        WhatsNewCommand = new RelayCommand(() => _updater?.OpenReleasePage(_verdict.Url));
        DismissUpdateCommand = new RelayCommand(DismissUpdate);
        CheckForUpdatesCommand = new RelayCommand(
            () => _ = CheckForUpdatesNowAsync(),
            () => _updater is not null && UpdateState is not (UpdateNoticeState.Downloading or UpdateNoticeState.Ready));
    }

    /// <summary>At launch: only when the setting is on and a day has passed since the last answer.</summary>
    public async Task CheckForUpdatesAtStartupAsync()
    {
        if (_updater is null || !_settings.CheckForUpdates || !UpdateSchedule.IsDue(_settings.LastUpdateCheckUtc, _clock()))
        {
            return;
        }

        await RunCheckAsync(sayWhatWasFound: false);
    }

    /// <summary>From the gear menu: ignores the setting and the schedule, and always reports.</summary>
    public async Task CheckForUpdatesNowAsync()
    {
        if (_updater is null || UpdateState is UpdateNoticeState.Downloading or UpdateNoticeState.Ready)
        {
            return;
        }

        await RunCheckAsync(sayWhatWasFound: true);
    }

    private async Task RunCheckAsync(bool sayWhatWasFound)
    {
        var verdict = await _updater!.CheckAsync();

        // Only an answer counts as a check. An unanswered one is simply tried again next launch.
        if (verdict.Outcome != UpdateOutcome.Unknown)
        {
            _settings = _settings with { LastUpdateCheckUtc = _clock() };
            _settingsStore.Save(_settings with { Style = _style });
        }

        if (verdict.IsAvailable)
        {
            var readiness = _updater.CanInstall();
            _verdict = verdict;
            _canInstallInPlace = readiness.IsStaged;
            _installBlockedReason = readiness.Detail;
            _updateFailure = null;
            UpdateState = UpdateNoticeState.Available;
            RaiseUpdateText();
            return;
        }

        if (sayWhatWasFound)
        {
            _dialogs.ShowInformation("Updates", verdict.Outcome == UpdateOutcome.UpToDate
                ? $"You have the latest version, v{_updater.CurrentVersion}."
                : "Could not check for updates right now. Check your internet connection and try again later.");
        }
    }

    private void OnUpdatePrimary()
    {
        switch (UpdateState)
        {
            case UpdateNoticeState.Ready:
                RestartToUpdate();
                break;
            case UpdateNoticeState.Available or UpdateNoticeState.Failed when !_canInstallInPlace:
                _updater?.OpenReleasePage(_verdict.Url);
                break;
            case UpdateNoticeState.Available or UpdateNoticeState.Failed:
                _ = StageUpdateAsync();
                break;
        }
    }

    /// <summary>Downloads and verifies. Nothing installed changes, whatever happens here.</summary>
    public async Task StageUpdateAsync()
    {
        if (_updater is null || _verdict.Release is null || UpdateState == UpdateNoticeState.Downloading)
        {
            return;
        }

        using var download = new CancellationTokenSource();
        _download = download;
        UpdateProgress = 0;
        UpdateState = UpdateNoticeState.Downloading;

        var result = await _updater.StageAsync(_verdict.Release, ProgressReporter(), download.Token);
        _download = null;

        if (result.IsStaged)
        {
            UpdateState = UpdateNoticeState.Ready;
        }
        else if (result.Outcome == InstallOutcome.Canceled)
        {
            UpdateState = UpdateNoticeState.None;
        }
        else
        {
            _updateFailure = result.Detail ?? "The download did not finish. Nothing was changed.";
            UpdateState = UpdateNoticeState.Failed;
            RaiseUpdateText();
        }
    }

    public void RestartToUpdate()
    {
        if (_updater is null || UpdateState != UpdateNoticeState.Ready)
        {
            return;
        }

        if (_updater.Apply(restart: true))
        {
            UpdateState = UpdateNoticeState.None;
            RestartRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _updateFailure = "The update could not be put in place. Nothing was changed.";
        UpdateState = UpdateNoticeState.Failed;
        RaiseUpdateText();
    }

    /// <summary>Called as the window closes: a ready update is installed without restarting.</summary>
    public void ApplyStagedUpdateOnExit()
    {
        if (_updater is not null && UpdateState == UpdateNoticeState.Ready)
        {
            _updater.Apply(restart: false);
        }
    }

    private void DismissUpdate()
    {
        _download?.Cancel();
        UpdateState = UpdateNoticeState.None;
    }

    private void ClearUpdateNoticeIfChecksWereTurnedOff()
    {
        // Available and Failed are what checking produced. Downloading and Ready are something
        // the user started, and turning off automatic checks does not undo that.
        if (!_settings.CheckForUpdates && UpdateState is UpdateNoticeState.Available or UpdateNoticeState.Failed)
        {
            UpdateState = UpdateNoticeState.None;
        }
    }

    private void RaiseUpdateText()
    {
        OnPropertyChanged(nameof(UpdateHeadline));
        OnPropertyChanged(nameof(UpdateDetail));
        OnPropertyChanged(nameof(UpdatePrimaryLabel));
        OnPropertyChanged(nameof(UpdateDismissLabel));
    }

    /// <summary>
    /// Applied directly, because <see cref="IUpdater.StageAsync"/> promises to report on the UI
    /// thread. Progress&lt;T&gt; was the obvious choice and the wrong one: it posts to whatever
    /// context it was created on, and xUnit 2 runs async tests under a context that forwards to
    /// the thread pool, so the report would race the assertion that reads it.
    /// </summary>
    private IProgress<double> ProgressReporter() => new DirectProgress(p => UpdateProgress = p);

    private sealed class DirectProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
```

- [ ] **Step 7: Run to verify they pass**

Run: `dotnet test tests/TrispotQR.ViewModels.Tests/TrispotQR.ViewModels.Tests.csproj`
Expected: PASS, 72 existing plus 17 new.

Run: `dotnet build TrispotQR.slnx`
Expected: 0 errors. The WPF app still compiles, because both new constructor parameters are optional.

- [ ] **Step 8: Commit**

```bash
git add src/TrispotQR.ViewModels tests/TrispotQR.ViewModels.Tests
git commit -m "feat(updates): the update notice and its state in the view model"
```

---

### Task 6: GitHubUpdater, the part that touches the network and the disk

**Files:**
- Create: `src/TrispotQR.UI/Services/GitHubUpdater.cs`
- Create: `src/TrispotQR.UI/Services/UpdateStartup.cs`
- Test: `tests/TrispotQR.UI.Tests/GitHubUpdaterTests.cs`

**Interfaces:**
- Consumes: `IUpdater`, `InstallResult`, `InstallOutcome` (Task 5); `ReleaseFeed`, `UpdateDecision`, `UpdateAssets`, `UpdatePlan` (Tasks 2 and 3); `AppInfo.Version` (existing, `src/TrispotQR.UI/Services/AppInfo.cs`).
- Produces:
  - `public sealed class GitHubUpdater : IUpdater` with constructor `GitHubUpdater(string? feedUrl = null, string? pageUrl = null, string? currentVersion = null, string? exePath = null, Func<string, CancellationToken, Task<Stream>>? open = null, Func<string, IReadOnlyList<string>, bool>? launch = null, Action<Action>? onUiThread = null, Func<bool>? isWindows = null, Action<string>? openBrowser = null)`; `static string? BuiltFeedUrl`, `static string? BuiltPageUrl` read from entry-assembly `AssemblyMetadata` keys `UpdateFeedUrl` and `UpdatePageUrl`.
  - `public static class UpdateStartup` with `const string UpdatedArgument = "--updated"`, `static void WaitForPredecessor(string[] args)`, `static void CleanUp(string? exePath = null)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text;
using TrispotQR.Core.Updates;
using TrispotQR.UI.Services;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Tests;

/// <summary>
/// The updater against a scratch folder of stand-in files and an in-memory "network". The rename
/// sequence is the one part of this feature that can leave a machine without a working app, and
/// against the real process path it cannot be exercised at all: a test would be replacing its own
/// runner.
/// </summary>
public sealed class GitHubUpdaterTests : IDisposable
{
    private const string Feed = "https://api.example.org/releases/latest";
    private const string ExeUrl = "https://example.org/TrispotQR.exe";
    private const string SumUrl = "https://example.org/TrispotQR.exe.sha256";

    private static readonly byte[] OldExe = Encoding.ASCII.GetBytes("old build");
    private static readonly byte[] NewExe = Encoding.ASCII.GetBytes("new build, a little longer");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"TrispotQR-update-{Guid.NewGuid():N}");
    private readonly Dictionary<string, byte[]> _web = [];
    private readonly List<string> _requested = [];
    private readonly List<(string Path, IReadOnlyList<string> Args)> _launched = [];
    private int _uiThreadHops;

    public GitHubUpdaterTests()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Exe, OldExe);
        _web[ExeUrl] = NewExe;
        _web[SumUrl] = Encoding.ASCII.GetBytes(HashOf(NewExe) + "  TrispotQR.exe\n");
        _web[Feed] = Encoding.UTF8.GetBytes(FeedJson("v9.9.9", ExeUrl, SumUrl));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Exe => Path.Combine(_folder, "TrispotQR.exe");

    private GitHubUpdater Updater(string feed = Feed, bool windows = true) => new(
        feedUrl: feed,
        pageUrl: "https://example.org/releases",
        currentVersion: "1.2.0",
        exePath: Exe,
        open: Open,
        launch: (path, args) =>
        {
            _launched.Add((path, args));
            return true;
        },
        onUiThread: work =>
        {
            _uiThreadHops++;
            work();
        },
        isWindows: () => windows);

    private Task<Stream> Open(string url, CancellationToken ct)
    {
        _requested.Add(url);
        return _web.TryGetValue(url, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes))
            : throw new HttpRequestException($"404 {url}");
    }

    private static string HashOf(byte[] bytes) =>
        UpdateAssets.Format(System.Security.Cryptography.SHA256.HashData(bytes));

    private static string FeedJson(string tag, string exeUrl, string? sumUrl)
    {
        var assets = new List<string>
        {
            $$"""{ "name": "TrispotQR.exe", "browser_download_url": "{{exeUrl}}", "size": {{NewExe.Length}}, "state": "uploaded" }""",
        };

        if (sumUrl is not null)
        {
            assets.Add($$"""{ "name": "TrispotQR.exe.sha256", "browser_download_url": "{{sumUrl}}", "size": 80, "state": "uploaded" }""");
        }

        return $$"""{ "tag_name": "{{tag}}", "html_url": "https://example.org/releases/{{tag}}", "assets": [{{string.Join(", ", assets)}}] }""";
    }

    private async Task<ReleaseInfo> FoundRelease(GitHubUpdater updater)
    {
        var verdict = await updater.CheckAsync();
        Assert.True(verdict.IsAvailable, "the feed should have offered v9.9.9");
        return verdict.Release!;
    }

    [Fact]
    public async Task CheckReadsTheFeedAndDecides()
    {
        var verdict = await Updater().CheckAsync();

        Assert.Equal(UpdateOutcome.Available, verdict.Outcome);
        Assert.Equal("9.9.9", verdict.Version.ToString());
    }

    [Fact]
    public async Task ACheckThatFailsIsUnknownAndNeverThrows()
    {
        _web.Remove(Feed);
        Assert.Equal(UpdateOutcome.Unknown, (await Updater().CheckAsync()).Outcome);

        _web[Feed] = Encoding.UTF8.GetBytes("<html>Sign in to the network</html>");
        Assert.Equal(UpdateOutcome.Unknown, (await Updater().CheckAsync()).Outcome);
    }

    [Theory]
    [InlineData("http://example.org/releases/latest")]
    [InlineData("")]
    public async Task AFeedThatIsNotHttpsIsNeverRequested(string feed)
    {
        var verdict = await Updater(feed).CheckAsync();

        Assert.Equal(UpdateOutcome.Unknown, verdict.Outcome);
        Assert.Empty(_requested);
    }

    [Fact]
    public void APublishedCopyInAWritableFolderCanInstall()
    {
        Assert.True(Updater().CanInstall().IsStaged);
    }

    [Fact]
    public void ADevelopmentBuildCannotInstall()
    {
        // A published build is one file. A build from source has its assemblies beside it, and a
        // published exe dropped among them would leave a folder that is half of each.
        File.WriteAllBytes(Path.Combine(_folder, "TrispotQR.Core.dll"), [1]);

        Assert.Equal(InstallOutcome.NotSupported, Updater().CanInstall().Outcome);
    }

    [Fact]
    public void OnlyWindowsInstallsInPlace()
    {
        Assert.Equal(InstallOutcome.NotSupported, Updater(windows: false).CanInstall().Outcome);
    }

    [Fact]
    public async Task StagingAMatchingDownloadLeavesItBesideTheAppAndTouchesNothingElse()
    {
        var updater = Updater();
        var progress = new List<double>();

        var result = await updater.StageAsync(await FoundRelease(updater), new Collector(progress));

        Assert.True(result.IsStaged, result.Detail);
        Assert.Equal(NewExe, File.ReadAllBytes(Exe + ".new"));
        Assert.Equal(OldExe, File.ReadAllBytes(Exe));
        Assert.Equal(1.0, progress[^1]);
        Assert.True(_uiThreadHops > 0, "progress must be marshalled to the UI thread");
    }

    [Fact]
    public async Task TheChecksumIsReadBeforeTheExeIsDownloaded()
    {
        var updater = Updater();
        var release = await FoundRelease(updater);
        _requested.Clear();

        await updater.StageAsync(release, null);

        Assert.Equal([SumUrl, ExeUrl], _requested);
    }

    [Fact]
    public async Task ADownloadThatDoesNotMatchIsDiscarded()
    {
        var updater = Updater();
        var release = await FoundRelease(updater);
        _web[ExeUrl] = Encoding.ASCII.GetBytes("tampered");

        var result = await updater.StageAsync(release, null);

        Assert.Equal(InstallOutcome.VerificationFailed, result.Outcome);
        Assert.False(File.Exists(Exe + ".new"));
        Assert.Equal(OldExe, File.ReadAllBytes(Exe));
    }

    [Fact]
    public async Task AReleaseWithNoChecksumIsNotDownloadedAtAll()
    {
        _web[Feed] = Encoding.UTF8.GetBytes(FeedJson("v9.9.9", ExeUrl, sumUrl: null));
        var updater = Updater();
        var release = await FoundRelease(updater);
        _requested.Clear();

        var result = await updater.StageAsync(release, null);

        Assert.Equal(InstallOutcome.VerificationFailed, result.Outcome);
        Assert.DoesNotContain(ExeUrl, _requested);
    }

    [Fact]
    public async Task AReleaseWithNoExeHasNothingToInstall()
    {
        var release = new ReleaseInfo("v9.9.9", "https://example.org", Assets: []);

        var result = await Updater().StageAsync(release, null);

        Assert.Equal(InstallOutcome.NothingToInstall, result.Outcome);
    }

    [Fact]
    public async Task AnAssetOverPlainHttpIsRefused()
    {
        const string insecure = "http://example.org/TrispotQR.exe";
        _web[Feed] = Encoding.UTF8.GetBytes(FeedJson("v9.9.9", insecure, SumUrl));
        _web[insecure] = NewExe;
        var updater = Updater();

        var result = await updater.StageAsync(await FoundRelease(updater), null);

        Assert.Equal(InstallOutcome.VerificationFailed, result.Outcome);
        Assert.DoesNotContain(insecure, _requested);
    }

    [Fact]
    public async Task ADownloadThatFailsLeavesNothingBehind()
    {
        var updater = Updater();
        var release = await FoundRelease(updater);
        _web.Remove(ExeUrl);

        var result = await updater.StageAsync(release, null);

        Assert.Equal(InstallOutcome.DownloadFailed, result.Outcome);
        Assert.False(File.Exists(Exe + ".new"));
    }

    [Fact]
    public async Task ApplySwapsTheExeAndRestartsWithTheOldProcessId()
    {
        var updater = Updater();
        await updater.StageAsync(await FoundRelease(updater), null);

        Assert.True(updater.Apply(restart: true));

        Assert.Equal(NewExe, File.ReadAllBytes(Exe));
        Assert.Equal(OldExe, File.ReadAllBytes(Exe + ".old"));
        Assert.False(File.Exists(Exe + ".new"));

        var (path, args) = Assert.Single(_launched);
        Assert.Equal(Exe, path);
        Assert.Equal([UpdateStartup.UpdatedArgument, Environment.ProcessId.ToString()], args);
    }

    [Fact]
    public async Task ApplyWithoutRestartInstallsAndStartsNothing()
    {
        var updater = Updater();
        await updater.StageAsync(await FoundRelease(updater), null);

        Assert.True(updater.Apply(restart: false));

        Assert.Equal(NewExe, File.ReadAllBytes(Exe));
        Assert.Empty(_launched);
    }

    [Fact]
    public void ApplyWithNothingStagedChangesNothing()
    {
        Assert.False(Updater().Apply(restart: true));
        Assert.Equal(OldExe, File.ReadAllBytes(Exe));
    }

    [Fact]
    public async Task WhenTheNewExeCannotBePutInPlaceTheOldOneIsPutBack()
    {
        // Holding the staged file open is how the second rename is made to fail. Only Windows
        // refuses to move an open file, so elsewhere there is no way to provoke this.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to rename an open file.");

        var updater = Updater();
        await updater.StageAsync(await FoundRelease(updater), null);

        bool applied;
        using (File.Open(Exe + ".new", FileMode.Open, FileAccess.Read, FileShare.None))
        {
            applied = updater.Apply(restart: true);
        }

        Assert.False(applied);
        Assert.Equal(OldExe, File.ReadAllBytes(Exe));
        Assert.Empty(_launched);
    }

    [Fact]
    public void CleanUpRemovesWhatAnUpdateLeftBehind()
    {
        File.WriteAllBytes(Exe + ".old", OldExe);
        File.WriteAllBytes(Exe + ".new", NewExe);

        UpdateStartup.CleanUp(Exe);

        Assert.False(File.Exists(Exe + ".old"));
        Assert.False(File.Exists(Exe + ".new"));
        Assert.True(File.Exists(Exe));
    }

    [Fact]
    public void WaitingForAProcessThatIsAlreadyGoneReturnsAtOnce()
    {
        var started = DateTime.UtcNow;

        UpdateStartup.WaitForPredecessor([UpdateStartup.UpdatedArgument, int.MaxValue.ToString()]);
        UpdateStartup.WaitForPredecessor(["--something-else"]);

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
    }

    private sealed class Collector(List<double> into) : IProgress<double>
    {
        public void Report(double value) => into.Add(value);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`
Expected: build FAILS, `GitHubUpdater` and `UpdateStartup` do not exist.

- [ ] **Step 3: Implement `UpdateStartup.cs`**

```csharp
using System.Diagnostics;
using TrispotQR.Core.Updates;

namespace TrispotQR.UI.Services;

/// <summary>What a launch does first, before anything could be holding the files.</summary>
public static class UpdateStartup
{
    /// <summary>Followed by the process id of the copy that was just replaced.</summary>
    public const string UpdatedArgument = "--updated";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Waits for the replaced copy to exit, so its backup file is free to delete. The common case
    /// is that it has already gone.
    /// </summary>
    public static void WaitForPredecessor(string[] args)
    {
        var at = Array.IndexOf(args, UpdatedArgument);
        if (at < 0 || at + 1 >= args.Length || !int.TryParse(args[at + 1], out var pid))
        {
            return;
        }

        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(Patience);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// Deletes a previous update's backup and any download that was never installed. A file still
    /// held by the exiting process is left for the next launch.
    /// </summary>
    public static void CleanUp(string? exePath = null)
    {
        var exe = exePath ?? Environment.ProcessPath;
        if (exe is null)
        {
            return;
        }

        foreach (var leftover in UpdatePlan.For(exe).Leftovers())
        {
            try
            {
                File.Delete(leftover);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
```

- [ ] **Step 4: Implement `GitHubUpdater.cs`**

```csharp
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia.Threading;
using TrispotQR.Core.Updates;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Services;

/// <summary>
/// Finds, downloads, verifies and installs a newer release of this app.
///
/// What leaves the machine: one HTTPS GET for a public file with no body, no query string and no
/// identifier, and the two downloads if the user clicks Update now. The User-Agent names the
/// product and version, which GitHub requires, and nothing else.
///
/// This downloads an executable and arranges for it to run, so everything here narrows that to
/// "the file the project published, or nothing": HTTPS only, the published SHA-256 checked before
/// anything moves, and an install of two renames ordered so no single failure leaves the machine
/// without a working copy. Adapted from Mullion's UpdateService and UpdateInstaller.
/// </summary>
public sealed class GitHubUpdater : IUpdater
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

    private static readonly HttpClient Http = CreateClient();

    private readonly string? _feedUrl;
    private readonly string? _pageUrl;
    private readonly string? _exePath;
    private readonly Func<string, CancellationToken, Task<Stream>> _open;
    private readonly Func<string, IReadOnlyList<string>, bool> _launch;
    private readonly Action<Action> _onUiThread;
    private readonly Func<bool> _isWindows;
    private readonly Action<string> _openBrowser;

    /// <param name="open">How a URL is fetched. Tests serve bytes from memory.</param>
    /// <param name="launch">How the replacement is started. Tests record instead of starting.</param>
    /// <param name="onUiThread">How progress reaches the UI thread. Tests run it inline.</param>
    public GitHubUpdater(
        string? feedUrl = null,
        string? pageUrl = null,
        string? currentVersion = null,
        string? exePath = null,
        Func<string, CancellationToken, Task<Stream>>? open = null,
        Func<string, IReadOnlyList<string>, bool>? launch = null,
        Action<Action>? onUiThread = null,
        Func<bool>? isWindows = null,
        Action<string>? openBrowser = null)
    {
        _feedUrl = feedUrl ?? BuiltFeedUrl;
        _pageUrl = pageUrl ?? BuiltPageUrl;
        CurrentVersion = currentVersion ?? AppInfo.Version;
        _exePath = exePath ?? Environment.ProcessPath;
        _open = open ?? OpenAsync;
        _launch = launch ?? Launch;
        _onUiThread = onUiThread ?? (work => Dispatcher.UIThread.Post(work));
        _isWindows = isWindows ?? OperatingSystem.IsWindows;
        _openBrowser = openBrowser ?? OpenInBrowser;
    }

    /// <summary>The release feed this build was published to read. Empty means never check.</summary>
    public static string? BuiltFeedUrl { get; } = ReadMetadata("UpdateFeedUrl");

    /// <summary>Where a person is sent to get a release by hand.</summary>
    public static string? BuiltPageUrl { get; } = ReadMetadata("UpdatePageUrl");

    public string CurrentVersion { get; }

    public async Task<UpdateVerdict> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            if (!UpdateAssets.IsAllowedUrl(_feedUrl))
            {
                return UpdateDecision.For(CurrentVersion, null);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CheckTimeout);

            await using var stream = await _open(_feedUrl!, timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync(timeout.Token).ConfigureAwait(false);

            return UpdateDecision.For(CurrentVersion, ReleaseFeed.Parse(json));
        }
        catch (Exception)
        {
            // Offline, DNS, a rate limit, a proxy, a timeout: all mean "could not tell". A
            // background check has no business raising anything into the app.
            return UpdateDecision.For(CurrentVersion, null);
        }
    }

    public InstallResult CanInstall()
    {
        if (!_isWindows())
        {
            return new(InstallOutcome.NotSupported, "Updating from inside the app works on Windows only. Download the new version instead.");
        }

        if (_exePath is null || !File.Exists(_exePath))
        {
            return new(InstallOutcome.NotSupported, "Trispot QR cannot find its own program file. Download the new version instead.");
        }

        var plan = UpdatePlan.For(_exePath);

        if (File.Exists(Path.Combine(plan.Directory, "TrispotQR.Core.dll")))
        {
            return new(InstallOutcome.NotSupported, "This is a development build. Update it from source.");
        }

        // A write probe rather than reading permissions: ACLs, UAC virtualization, controlled folder
        // access and read-only shares do not reduce to a flag anyone can read.
        var probe = Path.Combine(plan.Directory, $".trispotqr-write-test-{Environment.ProcessId}");

        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(InstallOutcome.NotWritable, "Trispot QR is in a folder it cannot update itself in. Download the new version instead, or move the app to a folder you can write to.");
        }

        return new(InstallOutcome.Staged);
    }

    public async Task<InstallResult> StageAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct = default)
    {
        var ready = CanInstall();
        if (!ready.IsStaged)
        {
            return ready;
        }

        var exe = UpdateAssets.Executable(release.Assets);
        if (exe is null)
        {
            return new(InstallOutcome.NothingToInstall, "That release has no TrispotQR.exe attached.");
        }

        var checksum = UpdateAssets.Checksum(release.Assets);
        if (!UpdateAssets.IsAllowedUrl(exe.Url) || (checksum is not null && !UpdateAssets.IsAllowedUrl(checksum.Url)))
        {
            return new(InstallOutcome.VerificationFailed, "That release points somewhere Trispot QR will not download from.");
        }

        var plan = UpdatePlan.For(_exePath!);
        var uiProgress = progress is null ? null : new UiThreadProgress(progress, _onUiThread);

        try
        {
            // The checksum first: downloading 50 MB before finding nothing to check it against
            // wastes the time and leaves the only question that matters unanswered.
            var expected = checksum is null ? null : await ReadChecksumAsync(checksum, ct).ConfigureAwait(false);

            if (expected is null)
            {
                return new(InstallOutcome.VerificationFailed, "That release publishes no checksum, so the download cannot be verified.");
            }

            var actual = await DownloadAsync(exe, plan.Staged, uiProgress, ct).ConfigureAwait(false);

            if (!UpdateAssets.Matches(expected, actual))
            {
                Discard(plan.Staged);
                return new(InstallOutcome.VerificationFailed, "The download did not match the published checksum, so it was discarded.");
            }

            return new(InstallOutcome.Staged);
        }
        catch (OperationCanceledException)
        {
            Discard(plan.Staged);
            return new(InstallOutcome.Canceled);
        }
        catch (Exception)
        {
            Discard(plan.Staged);
            return new(InstallOutcome.DownloadFailed, "The download did not finish. Nothing was changed.");
        }
    }

    /// <summary>
    /// Renames the running exe aside, renames the staged one into its place, and optionally starts
    /// it. If the second rename fails the first is undone. The window in which neither file holds
    /// the real name is one rename wide.
    /// </summary>
    public bool Apply(bool restart)
    {
        if (_exePath is null)
        {
            return false;
        }

        var plan = UpdatePlan.For(_exePath);

        if (!File.Exists(plan.Staged))
        {
            return false;
        }

        Discard(plan.Backup);

        try
        {
            File.Move(plan.Current, plan.Backup);
        }
        catch (Exception)
        {
            return false;
        }

        try
        {
            File.Move(plan.Staged, plan.Current);
        }
        catch (Exception)
        {
            try
            {
                File.Move(plan.Backup, plan.Current);
            }
            catch (Exception)
            {
                // Both names are now wrong. The backup is a working copy; renaming it by hand is
                // all that is needed, and there is no one to tell from inside a closing app.
            }

            return false;
        }

        if (restart)
        {
            // Installed either way. Failing to restart means the next launch gets it, which is a
            // worse experience but not a broken install.
            _launch(plan.Current, [UpdateStartup.UpdatedArgument, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }

        return true;
    }

    public void OpenReleasePage(string? url)
    {
        var target = url ?? _pageUrl;
        if (UpdateAssets.IsAllowedUrl(target))
        {
            _openBrowser(target!);
        }
    }

    private async Task<string?> ReadChecksumAsync(ReleaseAsset asset, CancellationToken ct)
    {
        await using var stream = await _open(asset.Url, ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        return UpdateAssets.TryReadChecksum(text, out var hash) ? hash : null;
    }

    /// <summary>
    /// Streams to disk hashing as it goes, so it is the bytes actually written that are vouched
    /// for, not a second read of a file something else could have touched.
    /// </summary>
    private async Task<string> DownloadAsync(ReleaseAsset asset, string destination, IProgress<double>? progress, CancellationToken ct)
    {
        Discard(destination);

        await using var source = await _open(asset.Url, ct).ConfigureAwait(false);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            var buffer = new byte[1 << 16];
            long total = 0;
            var lastPercent = -1;

            while (true)
            {
                var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > UpdateAssets.MostBytes)
                {
                    throw new IOException("The download is larger than a Trispot QR release can be.");
                }

                sha.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);

                // Whole percents only, so a fast download does not flood the UI thread.
                var percent = asset.Size > 0 ? (int)Math.Min(100, total * 100 / asset.Size) : -1;
                if (percent > lastPercent)
                {
                    lastPercent = percent;
                    progress?.Report(percent / 100.0);
                }
            }
        }

        progress?.Report(1);
        return UpdateAssets.Format(sha.GetHashAndReset());
    }

    private static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task<Stream> OpenAsync(string url, CancellationToken ct)
    {
        var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }

    private static bool Launch(string path, IReadOnlyList<string> args)
    {
        try
        {
            var start = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty };
            foreach (var arg in args)
            {
                start.ArgumentList.Add(arg);
            }

            return Process.Start(start) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TrispotQR", AppInfo.Version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static string? ReadMetadata(string key)
    {
        var value = (Assembly.GetEntryAssembly() ?? typeof(GitHubUpdater).Assembly)
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value;

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed class UiThreadProgress(IProgress<double> inner, Action<Action> onUiThread) : IProgress<double>
    {
        public void Report(double value) => onUiThread(() => inner.Report(value));
    }
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet build tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj` then `tests\TrispotQR.UI.Tests\bin\Debug\net10.0\TrispotQR.UI.Tests.exe`
Expected: 0 build errors; Total 264 (244 + 20 new), 0 failed; the rollback test skips off Windows. Confirm the total actually went up before trusting the result: an unchanged count means a stale binary.

- [ ] **Step 6: Commit**

```bash
git add src/TrispotQR.UI/Services/GitHubUpdater.cs src/TrispotQR.UI/Services/UpdateStartup.cs tests/TrispotQR.UI.Tests/GitHubUpdaterTests.cs
git commit -m "feat(updates): download, verify and install a release in place"
```

---

### Task 7: Startup cleanup, the feed address in the build, version 1.2.0

**Files:**
- Modify: `src/TrispotQR.Desktop/Program.cs`
- Modify: `src/TrispotQR.Desktop/TrispotQR.Desktop.csproj`

**Interfaces:**
- Consumes: `UpdateStartup` (Task 6).
- Produces: entry-assembly `AssemblyMetadata("UpdateFeedUrl", ...)` and `AssemblyMetadata("UpdatePageUrl", ...)` read by `GitHubUpdater.BuiltFeedUrl` and `BuiltPageUrl`; `-p:UpdateFeedUrl=` (empty) builds an app that never checks.

- [ ] **Step 1: Call the startup steps in `Program.cs`**

Replace `Main` with:

```csharp
    // Avalonia needs this on the main thread, before anything touches the toolkit.
    [STAThread]
    public static void Main(string[] args)
    {
        // Before anything else, so nothing in this process is holding the files. After an update
        // the replaced copy may still be exiting; once it has, its backup can be deleted.
        UpdateStartup.WaitForPredecessor(args);
        UpdateStartup.CleanUp();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
```

and add `using TrispotQR.UI.Services;` below `using TrispotQR.UI;`.

- [ ] **Step 2: Bake the feed address and bump the version in `TrispotQR.Desktop.csproj`**

Change `<Version>1.1.0</Version>` to `<Version>1.2.0</Version>`.

Add after the `RemoveNativePdbs` target:

```xml
  <!-- Where the app looks for a newer release, and where it sends people to get one by hand.
       Defaults to this project's own releases. Pass -p:UpdateFeedUrl=https://... to point a
       fork at its own, or -p:UpdateFeedUrl= with nothing after it to build an app that never
       checks: a global property that is set, even to empty, is not overridden by these lines.
       Written into the exe as assembly metadata, which GitHubUpdater reads back. A test runner
       is a different entry assembly with no such metadata, so the test suite never checks. -->
  <PropertyGroup>
    <UpdateFeedUrl Condition="'$(UpdateFeedUrl)' == ''">https://api.github.com/repos/levinium/TrispotQR/releases/latest</UpdateFeedUrl>
    <UpdatePageUrl Condition="'$(UpdatePageUrl)' == ''">https://github.com/levinium/TrispotQR/releases/latest</UpdatePageUrl>
  </PropertyGroup>

  <ItemGroup>
    <AssemblyMetadata Include="UpdateFeedUrl" Value="$(UpdateFeedUrl)" />
    <AssemblyMetadata Include="UpdatePageUrl" Value="$(UpdatePageUrl)" />
  </ItemGroup>
```

- [ ] **Step 3: Verify the metadata lands, and that an empty override removes it**

Run (PowerShell):

```powershell
dotnet build src/TrispotQR.Desktop/TrispotQR.Desktop.csproj -c Debug --nologo -v q
Select-String -Path src\TrispotQR.Desktop\bin\Debug\net10.0\TrispotQR.dll -Pattern 'api.github.com/repos/levinium/TrispotQR/releases/latest' -SimpleMatch -Quiet
dotnet build src/TrispotQR.Desktop/TrispotQR.Desktop.csproj -c Debug --nologo -v q -p:UpdateFeedUrl= -o $env:TEMP\trispot-nofeed
Select-String -Path $env:TEMP\trispot-nofeed\TrispotQR.dll -Pattern 'api.github.com/repos/levinium' -SimpleMatch -Quiet
```

Expected: both builds 0 errors; first `Select-String` prints `True`; second prints `False`.

- [ ] **Step 4: Commit**

```bash
git add src/TrispotQR.Desktop/Program.cs src/TrispotQR.Desktop/TrispotQR.Desktop.csproj
git commit -m "feat(updates): clean up after an update at launch, bake in the release feed, 1.2.0"
```

---

### Task 8: The notice and the menu entry in the main window

**Files:**
- Modify: `src/TrispotQR.UI/MainWindow.axaml` (after the version label and gear `StackPanel`, around line 225; gear `MenuFlyout` around line 218)
- Modify: `src/TrispotQR.UI/MainWindow.axaml.cs` (constructor, `OnClosing`, `WatchModel`, new handlers)
- Modify: `tests/TrispotQR.UI.Tests/UiHarness.cs` (`WithWindow` overloads, lines 67 to 109)
- Test: `tests/TrispotQR.UI.Tests/UpdateNoticeTests.cs`

**Interfaces:**
- Consumes: `MainViewModel` update members (Task 5), `GitHubUpdater` (Task 6).
- Produces: named controls `UpdateNotice`, `UpdateHeadlineText`, `UpdateDetailText`, `UpdateProgressBar`, `UpdatePrimaryButton`, `WhatsNewButton`, `DismissUpdateButton`; gear menu item with header `Check for updates`; `UiHarness.WithWindow(..., IDialogService? dialogs = null, IUpdater? updater = null)`.

- [ ] **Step 1: Let the harness hand a test updater to the model**

In `UiHarness.cs`, change the generic overload's signature to

```csharp
    public static T WithWindow<T>(Func<Session, T> work, IDialogService? dialogs = null, IUpdater? updater = null)
```

and its model construction to

```csharp
            var model = new MainViewModel(
                dialogs ?? new AvaloniaDialogService(window),
                new AvaloniaUiTimer(),
                new AvaloniaImageClipboard(window),
                new PresetStore(directory),
                new AppSettingsStore(directory),
                updater);
```

Change the `Action` overload to

```csharp
    public static void WithWindow(Action<Session> work, IDialogService? dialogs = null, IUpdater? updater = null) =>
        WithWindow<object?>(
            session =>
            {
                work(session);
                return null;
            },
            dialogs,
            updater);
```

Run the UI suite. Expected: 264 passed, nothing changed.

- [ ] **Step 2: Write the failing tests, `UpdateNoticeTests.cs`**

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TrispotQR.Core.Updates;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Tests;

/// <summary>
/// Whether a release that was found actually reaches the screen. The check is tested elsewhere;
/// this is the wiring, which fails silently: a working check and a notice nobody sees look the
/// same from outside.
///
/// Every updater here reports an update. A check that finds nothing opens a real modal message,
/// which nothing in a headless run can dismiss; that path is covered in MainViewModelTests.
/// </summary>
public class UpdateNoticeTests
{
    private static T Named<T>(Window window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException($"MainWindow has no {name}.");

    [AvaloniaFact]
    public void TheNoticeIsHiddenUntilThereIsSomethingToSay()
    {
        UiHarness.WithWindow(session =>
        {
            Assert.False(Named<Border>(session.Window, "UpdateNotice").IsVisible);
        });
    }

    [AvaloniaFact]
    public void AFoundReleaseShowsTheNoticeNamingTheVersion()
    {
        UiHarness.WithWindow(
            session =>
            {
                session.Model.CheckForUpdatesAtStartupAsync().GetAwaiter().GetResult();
                DispatcherPump.Drain();

                Assert.True(Named<Border>(session.Window, "UpdateNotice").IsVisible);
                Assert.Contains("9.9.9", Named<TextBlock>(session.Window, "UpdateHeadlineText").Text);
                Assert.Equal("Update now", Named<Button>(session.Window, "UpdatePrimaryButton").Content);
                Assert.Equal("Later", Named<Button>(session.Window, "DismissUpdateButton").Content);
            },
            updater: new AlwaysNewer());
    }

    [AvaloniaFact]
    public void UpdateNowStagesAndTheButtonBecomesRestart()
    {
        UiHarness.WithWindow(
            session =>
            {
                session.Model.CheckForUpdatesAtStartupAsync().GetAwaiter().GetResult();
                DispatcherPump.Drain();

                var primary = Named<Button>(session.Window, "UpdatePrimaryButton");
                primary.Command!.Execute(null);
                DispatcherPump.Drain();

                Assert.Equal("Restart now", primary.Content);
                Assert.False(Named<Button>(session.Window, "DismissUpdateButton").IsVisible);
                Assert.Contains("close", Named<TextBlock>(session.Window, "UpdateDetailText").Text);
            },
            updater: new AlwaysNewer());
    }

    [AvaloniaFact]
    public void LaterHidesTheNotice()
    {
        UiHarness.WithWindow(
            session =>
            {
                session.Model.CheckForUpdatesAtStartupAsync().GetAwaiter().GetResult();
                DispatcherPump.Drain();

                Named<Button>(session.Window, "DismissUpdateButton").Command!.Execute(null);
                DispatcherPump.Drain();

                Assert.False(Named<Border>(session.Window, "UpdateNotice").IsVisible);
            },
            updater: new AlwaysNewer());
    }

    [AvaloniaFact]
    public void TheGearMenuOffersACheckAndItRuns()
    {
        var updater = new AlwaysNewer();

        UiHarness.WithWindow(
            session =>
            {
                // Relative, not absolute: opening the window already ran the startup check,
                // which is its own wiring working, not this entry's.
                var before = updater.Checks;

                var gear = Named<Button>(session.Window, "GearButton");
                UiHarness.Click(session.Window, UiHarness.At(gear, 0.5, 0.5));
                DispatcherPump.Drain();

                var item = Assert.Single(
                    session.Window.GetVisualDescendants().OfType<MenuItem>(),
                    i => (i.Header as string) == "Check for updates");
                UiHarness.Click(session.Window, UiHarness.At(item, 0.5, 0.5));
                DispatcherPump.Drain();

                Assert.Equal(before + 1, updater.Checks);
                Assert.True(Named<Border>(session.Window, "UpdateNotice").IsVisible);
            },
            updater: updater);
    }

    private sealed class AlwaysNewer : IUpdater
    {
        public int Checks { get; private set; }

        public string CurrentVersion => "1.2.0";

        public Task<UpdateVerdict> CheckAsync(CancellationToken ct = default)
        {
            Checks++;
            return Task.FromResult(UpdateDecision.For("1.2.0", new ReleaseInfo("v9.9.9", "https://example.org/releases/v9.9.9", Assets: [])));
        }

        public InstallResult CanInstall() => new(InstallOutcome.Staged);

        public Task<InstallResult> StageAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct = default) =>
            Task.FromResult(new InstallResult(InstallOutcome.Staged));

        public bool Apply(bool restart) => true;

        public void OpenReleasePage(string? url)
        {
        }
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: build the UI tests and run the exe.
Expected: 0 build errors; `UpdateNoticeTests` fail with "MainWindow has no UpdateNotice" and no "Check for updates" item.

- [ ] **Step 4: Add the notice and menu item to `MainWindow.axaml`**

Inside the gear `MenuFlyout`, before `<MenuItem Header="Settings..." Click="OnSettingsClicked" />`, add:

```xml
                <MenuItem Header="Check for updates" Click="OnCheckForUpdatesClicked"
                          IsVisible="{Binding CanCheckForUpdates}" />
```

Directly after the `StackPanel` that holds `VersionLabel` and `GearButton` (the one closing around line 225), add:

```xml
        <!-- Shown only when a newer release exists. Nothing is ever on screen to report that
             there is no news. Its text and buttons change as the update moves from available
             through downloading to ready; the view model owns those states. -->
        <Border x:Name="UpdateNotice" IsVisible="{Binding IsUpdateNoticeVisible}"
                Background="{DynamicResource SurfaceBrush}"
                BorderBrush="{DynamicResource AccentBrush}" BorderThickness="1"
                CornerRadius="6" Padding="12">
          <StackPanel Spacing="8">
            <TextBlock x:Name="UpdateHeadlineText" Text="{Binding UpdateHeadline}"
                       FontWeight="SemiBold" TextWrapping="Wrap" />
            <TextBlock x:Name="UpdateDetailText" Text="{Binding UpdateDetail}"
                       TextWrapping="Wrap" FontSize="12"
                       Foreground="{DynamicResource MutedTextBrush}"
                       IsVisible="{Binding UpdateDetail, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
            <ProgressBar x:Name="UpdateProgressBar" Minimum="0" Maximum="1"
                         Value="{Binding UpdateProgress}" IsVisible="{Binding IsUpdateDownloading}" />
            <StackPanel Orientation="Horizontal" Spacing="8">
              <Button x:Name="UpdatePrimaryButton" Content="{Binding UpdatePrimaryLabel}"
                      Command="{Binding UpdatePrimaryCommand}"
                      IsVisible="{Binding !IsUpdateDownloading}" />
              <Button x:Name="WhatsNewButton" Content="What's new"
                      Command="{Binding WhatsNewCommand}" />
              <Button x:Name="DismissUpdateButton" Content="{Binding UpdateDismissLabel}"
                      Command="{Binding DismissUpdateCommand}"
                      IsVisible="{Binding CanDismissUpdate}" />
            </StackPanel>
          </StackPanel>
        </Border>
```

- [ ] **Step 5: Compose, check at open, close for a restart, install on close, in `MainWindow.axaml.cs`**

In the constructor, change the model construction to pass the real updater:

```csharp
        _model = new MainViewModel(
            new AvaloniaDialogService(this),
            new AvaloniaUiTimer(),
            new AvaloniaImageClipboard(this),
            updater: new GitHubUpdater());
```

At the end of the constructor, add:

```csharp
        // After the window is up, so a slow network never delays the first paint. The check
        // never throws. Asked of DataContext, like every other handler here, so the tests'
        // substituted model is the one asked.
        Opened += async (_, _) =>
        {
            if (DataContext is MainViewModel model)
            {
                await model.CheckForUpdatesAtStartupAsync();
            }
        };
```

In `WatchModel`, add `_watchedModel.RestartRequested -= OnRestartRequested;` beside the other two unsubscriptions and `_watchedModel.RestartRequested += OnRestartRequested;` beside the other two subscriptions.

Replace `OnClosing` with:

```csharp
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _model.SaveSession(Width, Height);

        // After the session is saved. A downloaded update that was never restarted into is put
        // in place now, so the next launch is the new version and nobody lost what they typed.
        _watchedModel?.ApplyStagedUpdateOnExit();

        base.OnClosing(e);
    }
```

Add handlers beside `OnSettingsClicked`:

```csharp
    /// <summary>The new version is already in place and started; closing normally saves the session.</summary>
    private void OnRestartRequested(object? sender, EventArgs e) => Close();

    /// <summary>async void because that is what a click handler is; the check never throws.</summary>
    private async void OnCheckForUpdatesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model)
        {
            await model.CheckForUpdatesNowAsync();
        }
    }
```

- [ ] **Step 6: Run to verify they pass**

Run: build the UI tests and run the exe.
Expected: 0 build errors; Total 269, 0 failed. `TheMainWindowIsPaintedFromThePaletteRatherThanFromLiteralColours` and the heading/label tests in `StylingPanelTests` still pass; if a structural test counts top-level elements of the form, update it to include `UpdateNotice` and say why in its comment.

- [ ] **Step 7: Commit**

```bash
git add src/TrispotQR.UI/MainWindow.axaml src/TrispotQR.UI/MainWindow.axaml.cs tests/TrispotQR.UI.Tests/UiHarness.cs tests/TrispotQR.UI.Tests/UpdateNoticeTests.cs
git commit -m "feat(updates): show the update notice and a manual check in the main window"
```

---

### Task 9: The Updates switch in Settings

**Files:**
- Modify: `src/TrispotQR.UI/Views/SettingsWindow.axaml` (new card after "Starting up", around line 103)
- Modify: `src/TrispotQR.UI/Views/SettingsWindow.axaml.cs` (constructor, `OnResetDefaults`, `OnDone`)
- Test: `tests/TrispotQR.UI.Tests/SettingsWindowTests.cs` (append tests; helpers `WithSettings`, `Toggle`, `Press` already exist there)

**Interfaces:**
- Consumes: `AppSettings.CheckForUpdates` (Task 4).
- Produces: `CheckBox x:Name="CheckUpdates"`.

- [ ] **Step 1: Write the failing tests**

Append to `SettingsWindowTests`:

```csharp
    [AvaloniaFact]
    public void TheUpdatesSwitchShowsWhatWasHandedIn()
    {
        WithSettings(AppSettings.Default with { CheckForUpdates = false }, window =>
        {
            Assert.False(Toggle(window, "CheckUpdates").IsChecked);
        });

        WithSettings(AppSettings.Default, window =>
        {
            Assert.True(Toggle(window, "CheckUpdates").IsChecked);
        });
    }

    [AvaloniaFact]
    public void TurningUpdatesOffCarriesBackOutAndKeepsWhenItLastChecked()
    {
        var checkedAt = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

        WithSettings(AppSettings.Default with { LastUpdateCheckUtc = checkedAt }, window =>
        {
            Press(window, "CheckUpdates");
            Press(window, "DoneButton");

            Assert.False(window.Result.CheckForUpdates);

            // Session state this window has no control for, and must not throw away.
            Assert.Equal(checkedAt, window.Result.LastUpdateCheckUtc);
        });
    }

    [AvaloniaFact]
    public void ResetToDefaultsTurnsUpdatesBackOn()
    {
        WithSettings(AppSettings.Default with { CheckForUpdates = false }, window =>
        {
            Press(window, "ResetDefaultsButton");

            Assert.True(Toggle(window, "CheckUpdates").IsChecked);
        });
    }
```

- [ ] **Step 2: Run to verify they fail**

Expected: the three new tests fail; there is no `CheckUpdates` control.

- [ ] **Step 3: Add the card to `SettingsWindow.axaml`**

After the "Starting up" card's closing `</Border>`:

```xml
    <Border Classes="card">
      <StackPanel Spacing="8">
        <TextBlock Text="Updates" Classes="heading"/>
        <CheckBox x:Name="CheckUpdates" Content="Check for new versions automatically"/>
        <TextBlock Text="Once a day, Trispot QR asks GitHub whether a newer version exists. Nothing about you or your codes is sent. You can also check from the gear menu."
                   Classes="caption" Margin="24,0,0,0"/>
      </StackPanel>
    </Border>
```

- [ ] **Step 4: Read and write it in `SettingsWindow.axaml.cs`**

In the constructor, after `RememberStyle.IsChecked = settings.RememberLastStyle;`:

```csharp
        CheckUpdates.IsChecked = settings.CheckForUpdates;
```

In `OnResetDefaults`, after `RememberStyle.IsChecked = defaults.RememberLastStyle;`:

```csharp
        CheckUpdates.IsChecked = defaults.CheckForUpdates;
```

In `OnDone`, inside the `with` block after `RememberLastStyle = ...`:

```csharp
            CheckForUpdates = CheckUpdates.IsChecked == true,
```

- [ ] **Step 5: Run to verify they pass**

Expected: UI Total 272, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/TrispotQR.UI/Views/SettingsWindow.axaml src/TrispotQR.UI/Views/SettingsWindow.axaml.cs tests/TrispotQR.UI.Tests/SettingsWindowTests.cs
git commit -m "feat(updates): a Settings switch for the automatic check"
```

---

### Task 10: Single-exe publish, tagged release workflow, docs

**Files:**
- Modify (replace whole file): `publish.ps1`
- Create: `.github/workflows/release.yml`
- Modify: `README.md` (the `## Download` section through the end of `### Mac and Linux`, and the test counts under `## Building from source`)
- Modify: `CHANGELOG.md` (release steps near the top; new `## 1.2.0` section above `## 1.1.0`)

**Interfaces:**
- Consumes: `UpdateAssets.ExeName`, `UpdateAssets.ChecksumName` values (Task 3); `UpdateFeedUrl`/`UpdatePageUrl` properties (Task 7).
- Produces: `dist\TrispotQR.exe` and `dist\TrispotQR.exe.sha256`; a draft GitHub release on each `v*` tag.

- [ ] **Step 1: Replace `publish.ps1`**

```powershell
<#
.SYNOPSIS
    Builds TrispotQR.exe for distribution, with its checksum.

.DESCRIPTION
    Runs every test suite, then produces exactly two files in dist\:

      dist\TrispotQR.exe          self-contained single file, needs nothing installed
      dist\TrispotQR.exe.sha256   its SHA-256, in sha256sum format

    These are the files a release publishes, and the same two the app's updater downloads: it
    refuses an exe whose hash does not match the checksum published beside it. The release
    workflow runs this script, so a desk build and a published one are made the same way.

    The framework-dependent build is gone as of 1.2.0. It needed native libraries beside the
    exe, and an updater that replaces one file cannot safely update a folder of them.

.PARAMETER SkipTests
    Publishes without running the tests first. Use only when the suite has just passed.

.PARAMETER UpdateFeedUrl
    Where the built app looks for newer releases. Defaults to this project's own. Pass a fork's
    feed, a local test server's address, or "" to build an app that never checks.

.PARAMETER UpdatePageUrl
    Where the app sends people to download by hand. Only meaningful with -UpdateFeedUrl.
#>

[CmdletBinding()]
param(
    [switch]$SkipTests,
    [string]$UpdateFeedUrl,
    [string]$UpdatePageUrl,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src\TrispotQR.Desktop\TrispotQR.Desktop.csproj'
$dist = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $root 'dist' }

# Where-Object because the file has several PropertyGroups and only one carries a Version.
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ }
if (-not $version) { throw 'No <Version> found in the project file.' }
Write-Host "Building Trispot QR v$version" -ForegroundColor Cyan

if (-not $SkipTests) {
    Write-Host 'Running tests...' -ForegroundColor Cyan
    dotnet test (Join-Path $root 'TrispotQR.slnx') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed. Nothing was published.' }
}

# PSBoundParameters rather than truthiness: "" is a meaning here (never check), and a plain
# `if ($UpdateFeedUrl)` cannot tell it apart from not passing the parameter at all. [string[]]
# and the leading commas keep a one-element array from unrolling into a string that splats one
# character at a time.
[string[]] $updates = @()
if ($PSBoundParameters.ContainsKey('UpdateFeedUrl')) {
    $updates += , "-p:UpdateFeedUrl=$UpdateFeedUrl"
    $page = if ($PSBoundParameters.ContainsKey('UpdatePageUrl')) { $UpdatePageUrl } else { $UpdateFeedUrl }
    $updates += , "-p:UpdatePageUrl=$page"
    Write-Host "  Update feed: $(if ($UpdateFeedUrl) { $UpdateFeedUrl } else { 'none, this build never checks' })"
}

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

Write-Host 'Publishing...' -ForegroundColor Cyan
dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $dist `
    --nologo @updates
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$exe = Join-Path $dist 'TrispotQR.exe'
if (-not (Test-Path $exe)) { throw "Expected $exe but it is not there." }

# Anything else beside the exe means the single file is not single, and the updater, which
# replaces only the exe, would leave those files stale.
$strays = Get-ChildItem $dist -File | Where-Object { $_.Name -ne 'TrispotQR.exe' }
if ($strays) { throw "Unexpected files beside the exe: $($strays.Name -join ', ')" }

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  TrispotQR.exe" | Out-File -FilePath (Join-Path $dist 'TrispotQR.exe.sha256') -Encoding ascii -NoNewline

Write-Host ''
Write-Host "Done. Published Trispot QR v$version" -ForegroundColor Green
Write-Host ("  {0}  {1:N1} MB" -f $exe, ((Get-Item $exe).Length / 1MB))
Write-Host "  SHA256 $hash"
```

- [ ] **Step 2: Verify the publish**

Run: `.\publish.ps1 -SkipTests`
Expected: `dist\` contains exactly `TrispotQR.exe` and `TrispotQR.exe.sha256`; the hash in the file equals `(Get-FileHash dist\TrispotQR.exe).Hash` compared case-insensitively; the exe launches and its version label reads `v1.2.0`.

- [ ] **Step 3: Create `.github/workflows/release.yml`**

```yaml
# Publishes a draft release when a version tag is pushed:
#
#   git tag v1.2.0 && git push origin v1.2.0
#
# The app's updater reads the tag as the version, so the tag must be "v" plus <Version> in
# TrispotQR.Desktop.csproj. This job checks that rather than trusting it: a mismatch would
# publish an exe that reports an older version, and every copy would keep offering the update
# it had just installed.
#
# A DRAFT, so the notes can be written and the files checked before anyone's app can see it:
# drafts are invisible to the public releases API the updater reads.
name: Release

on:
  push:
    tags: ["v*"]

permissions:
  contents: write

jobs:
  release:
    runs-on: windows-latest

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Check the tag matches the version in the build
        shell: pwsh
        run: |
          $version = ([xml](Get-Content src/TrispotQR.Desktop/TrispotQR.Desktop.csproj)).Project.PropertyGroup.Version | Where-Object { $_ }
          $tag = "${{ github.ref_name }}" -replace '^v', ''
          if ($version -ne $tag) {
            throw "Tag $tag does not match <Version> $version in TrispotQR.Desktop.csproj. Bump the version, or retag."
          }
          Write-Host "Version $version matches the tag."

      # publish.ps1 runs every test suite first and refuses to produce an exe if any fail, then
      # writes the checksum beside it.
      - name: Test and publish
        shell: pwsh
        run: ./publish.ps1

      - name: Create the draft release
        uses: softprops/action-gh-release@v3
        with:
          draft: true
          generate_release_notes: true
          files: |
            dist/TrispotQR.exe
            dist/TrispotQR.exe.sha256
```

This workflow cannot run until a tag is pushed, which happens only after release is approved. Before committing, check it by reading: the version expression is the same one `publish.ps1` uses (Step 1), and the two file paths match Step 2's output.

- [ ] **Step 4: Update `README.md`**

Replace everything from `## Download` up to (not including) `## Why not just use a website?` with:

````markdown
## Download

**[Download TrispotQR.exe](https://github.com/levinium/TrispotQR/releases/latest/download/TrispotQR.exe)** (about 48 MB)

Save it anywhere and run it. That is the whole installation. There is no setup step, no admin
prompt and no registry entry, because the .NET runtime it needs is inside the file. Delete it and
it is gone. You need 64-bit Windows and nothing else.

The app is not code-signed, so the first time you run it Windows may say it protected your PC.
Choose **More info**, then **Run anyway**. To confirm you have exactly the file this project
published, compare its fingerprint with the `TrispotQR.exe.sha256` file on the
[release page](https://github.com/levinium/TrispotQR/releases/latest):

```powershell
Get-FileHash .\TrispotQR.exe -Algorithm SHA256
```

### Updates

Once a day, when it starts, Trispot QR asks GitHub whether a newer version has been published,
and shows a notice only if one has. **Update now** downloads it, checks it against the published
fingerprint and swaps it in, either straight away with **Restart now** or when you next close the
app. The check is one request for a public file; nothing about you, your machine or your codes is
sent. Turn it off in Settings, or check by hand from the gear menu.

Coming from 1.0.0 or 1.1.0, download 1.2.0 once by hand, since those versions predate the updater.
Saved styles and settings carry over.

### Mac and Linux

Close, but not yet released. The app is built on Avalonia, which is what makes other platforms
possible at all, and CI builds the desktop app and runs its whole test suite on Linux and macOS
as well as Windows on every push.

What is missing is not code but evidence: nobody has yet opened the built app on either platform,
and a build that compiles and passes headless tests is not the same as one whose file dialogs,
clipboard and fonts have been seen to work. Those builds ship once that has actually been checked.

````

Under `## Building from source`, replace `dotnet test          # 960 tests: 740 run on Windows, Linux and macOS` with the totals from the final verification run in Task 11, Step 1: the sum of all four suites, and the sum of Core, ViewModels and UI.

Replace the line `.\publish.ps1        # builds dist\TrispotQR.exe` with `.\publish.ps1        # builds dist\TrispotQR.exe and its checksum`.

- [ ] **Step 5: Update `CHANGELOG.md`**

Replace the numbered release steps (`1. Bump <Version>` through `4. Copy dist\TrispotQR.exe wherever it is going.`) with:

```markdown
1. Bump `<Version>` in `src\TrispotQR.Desktop\TrispotQR.Desktop.csproj`.
2. Add a section below describing what changed.
3. Commit, then tag and push: `git tag v1.2.3` and `git push origin main v1.2.3`.
4. The Release workflow refuses a tag that does not match the version, runs every test,
   publishes `TrispotQR.exe` with its checksum, and creates a **draft** release.
5. Edit the draft's notes, check the files, and publish it. Installed copies see it from then on.

`.\publish.ps1` builds the same two files locally, for testing a build before tagging it.
```

Add above `## 1.1.0`:

```markdown
## 1.2.0

**Updates arrive through the app.** Once a day at launch Trispot QR asks GitHub whether a newer
version exists and shows a notice only if one does. Update now downloads it, verifies it against
the checksum published with the release, and swaps it in, either on Restart now or when the app
next closes, so nothing typed is lost. The check sends nothing about you or your machine. It can be
turned off in Settings, and run by hand from the gear menu.

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
```

- [ ] **Step 6: Commit**

```bash
git add publish.ps1 .github/workflows/release.yml README.md CHANGELOG.md
git commit -m "build: release TrispotQR.exe and its checksum from a tagged workflow; document updates"
```

---

### Task 11: Full verification and the end-to-end update, on this machine

**Files:** none changed unless a defect is found. Scratch files live under `%TEMP%\trispot-e2e`.

This task proves in the real app what no automated test here can: that a published build finds a
release, downloads it, replaces its own exe, restarts as the new version and cleans up. It runs
against a local server, so nothing is published. Several steps are done by a person at the screen.

- [ ] **Step 1: Clean build and every suite**

```powershell
Get-Process TrispotQR -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build TrispotQR.slnx -c Debug --nologo -v q --no-incremental
foreach ($p in 'Core.Tests','ViewModels.Tests','Tests') { dotnet test "tests/TrispotQR.$p/TrispotQR.$p.csproj" -c Debug --no-build --nologo -v q }
tests\TrispotQR.UI.Tests\bin\Debug\net10.0\TrispotQR.UI.Tests.exe
```

Expected: 0 errors, no warnings beyond the 11 existing xUnit2017 ones, every suite green. Record the four totals for README (Task 10, Step 4) and amend that commit if the numbers differ from what was written.

- [ ] **Step 2: Back up the real settings folder**

```powershell
$e2e = Join-Path $env:TEMP 'trispot-e2e'
if (Test-Path $e2e) { [IO.Directory]::Delete($e2e, $true) }
New-Item -ItemType Directory -Force $e2e | Out-Null
Copy-Item (Join-Path $env:APPDATA 'TrispotQR') (Join-Path $e2e 'settings-backup') -Recurse
```

- [ ] **Step 3: Build the "installed" copy and the "released" copy**

```powershell
$installed = Join-Path $e2e 'installed'
$served = Join-Path $e2e 'served'
.\publish.ps1 -SkipTests -OutputDirectory $installed -UpdateFeedUrl 'http://localhost:8765/release.json' -UpdatePageUrl 'http://localhost:8765/'
dotnet publish src/TrispotQR.Desktop/TrispotQR.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:Version=9.9.9 -o $served --nologo
Remove-Item (Join-Path $installed 'TrispotQR.exe.sha256')
Copy-Item (Join-Path $installed 'TrispotQR.exe') (Join-Path $e2e 'installed-original.exe')
$hash = (Get-FileHash (Join-Path $served 'TrispotQR.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  TrispotQR.exe" | Out-File (Join-Path $served 'TrispotQR.exe.sha256') -Encoding ascii -NoNewline
$size = (Get-Item (Join-Path $served 'TrispotQR.exe')).Length
@"
{ "tag_name": "v9.9.9", "html_url": "http://localhost:8765/", "draft": false, "prerelease": false,
  "assets": [
    { "name": "TrispotQR.exe", "browser_download_url": "http://localhost:8765/TrispotQR.exe", "size": $size, "state": "uploaded" },
    { "name": "TrispotQR.exe.sha256", "browser_download_url": "http://localhost:8765/TrispotQR.exe.sha256", "size": 80, "state": "uploaded" } ] }
"@ | Set-Content (Join-Path $served 'release.json') -Encoding utf8NoBOM
```

Expected: `installed\TrispotQR.exe` reports v1.2.0; `served\` holds `TrispotQR.exe` (v9.9.9), its checksum and `release.json`.

- [ ] **Step 4: Serve the fake release (run in the background)**

```powershell
$served = Join-Path $env:TEMP 'trispot-e2e\served'
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add('http://localhost:8765/')
$listener.Start()
while ($listener.IsListening) {
    $context = $listener.GetContext()
    $file = Join-Path $served ($context.Request.Url.AbsolutePath.TrimStart('/'))
    if (Test-Path $file -PathType Leaf) {
        $bytes = [IO.File]::ReadAllBytes($file)
        $context.Response.ContentLength64 = $bytes.Length
        $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
    } else {
        $context.Response.StatusCode = 404
    }
    $context.Response.Close()
}
```

Expected: `Invoke-WebRequest http://localhost:8765/release.json` from another shell returns the JSON.

- [ ] **Step 5: With the person at the screen, the happy path**

Launch `installed\TrispotQR.exe`. Then:

1. Within a few seconds a notice reads "Trispot QR 9.9.9 is available." If the real settings recorded a check today, use gear, then **Check for updates**, instead.
2. **Update now** shows a progress bar climbing to 100%, then "Trispot QR 9.9.9 is ready." with **Restart now**.
3. **Restart now**: the window closes and reopens, and the version label reads **v9.9.9**.
4. After a few seconds, `installed\` contains `TrispotQR.exe` only, with no `.old` or `.new`.

- [ ] **Step 6: A download that does not match is refused**

Close the app. Restore the original: `Copy-Item $e2e\installed-original.exe $installed\TrispotQR.exe -Force`. Overwrite `served\TrispotQR.exe.sha256` with `0000000000000000000000000000000000000000000000000000000000000000  TrispotQR.exe`. Launch, use gear, then **Check for updates**, then **Update now**.

Expected: the notice says the download did not match the published checksum; **Try again** is offered; `installed\` holds only the original exe, still v1.2.0. Put the real checksum back afterwards (rerun the two checksum lines from Step 3).

- [ ] **Step 7: Install on close, without restarting**

Launch, **Check for updates**, **Update now**, wait for ready, then close the window with the X. Relaunch `installed\TrispotQR.exe`.

Expected: it opens as v9.9.9.

- [ ] **Step 8: A folder the app cannot write to offers Download instead**

```powershell
Copy-Item $e2e\installed-original.exe $installed\TrispotQR.exe -Force
icacls $installed /deny "$($env:USERNAME):(W,D)"
```

Launch, **Check for updates**. Expected: the primary button reads **Download** and the notice explains the folder cannot be written to; clicking it opens `http://localhost:8765/` in the browser. Then close the app and remove the denial: `icacls $installed /remove:d "$env:USERNAME"`.

- [ ] **Step 9: Put everything back**

Stop the listener (Ctrl+C in its shell). Close any TrispotQR. Then:

```powershell
$live = Join-Path $env:APPDATA 'TrispotQR'
[IO.Directory]::Delete($live, $true)
Copy-Item (Join-Path $e2e 'settings-backup') $live -Recurse
```

Expected: the real favorites and settings are as they were before Step 2.

- [ ] **Step 10: Record the result**

Note in the branch's final report which of Steps 5 to 8 passed as described, and anything that did not. Merging, pushing and tagging v1.2.0 are separate decisions for the user and are not part of this plan.
