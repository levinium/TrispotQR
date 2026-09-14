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
        var verdict = await updater.CheckAsync(TestContext.Current.CancellationToken);
        Assert.True(verdict.IsAvailable, "the feed should have offered v9.9.9");
        return verdict.Release!;
    }

    [Fact]
    public async Task CheckReadsTheFeedAndDecides()
    {
        var verdict = await Updater().CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateOutcome.Available, verdict.Outcome);
        Assert.Equal("9.9.9", verdict.Version.ToString());
    }

    [Fact]
    public async Task ACheckThatFailsIsUnknownAndNeverThrows()
    {
        _web.Remove(Feed);
        Assert.Equal(UpdateOutcome.Unknown, (await Updater().CheckAsync(TestContext.Current.CancellationToken)).Outcome);

        _web[Feed] = Encoding.UTF8.GetBytes("<html>Sign in to the network</html>");
        Assert.Equal(UpdateOutcome.Unknown, (await Updater().CheckAsync(TestContext.Current.CancellationToken)).Outcome);
    }

    [Theory]
    [InlineData("http://example.org/releases/latest")]
    [InlineData("")]
    public async Task AFeedThatIsNotHttpsIsNeverRequested(string feed)
    {
        var verdict = await Updater(feed).CheckAsync(TestContext.Current.CancellationToken);

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

        var result = await updater.StageAsync(await FoundRelease(updater), new Collector(progress), TestContext.Current.CancellationToken);

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

        await updater.StageAsync(release, null, TestContext.Current.CancellationToken);

        Assert.Equal([SumUrl, ExeUrl], _requested);
    }

    [Fact]
    public async Task ADownloadThatDoesNotMatchIsDiscarded()
    {
        var updater = Updater();
        var release = await FoundRelease(updater);
        _web[ExeUrl] = Encoding.ASCII.GetBytes("tampered");

        var result = await updater.StageAsync(release, null, TestContext.Current.CancellationToken);

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

        var result = await updater.StageAsync(release, null, TestContext.Current.CancellationToken);

        Assert.Equal(InstallOutcome.VerificationFailed, result.Outcome);
        Assert.DoesNotContain(ExeUrl, _requested);
    }

    [Fact]
    public async Task AReleaseWithNoExeHasNothingToInstall()
    {
        var release = new ReleaseInfo("v9.9.9", "https://example.org", Assets: []);

        var result = await Updater().StageAsync(release, null, TestContext.Current.CancellationToken);

        Assert.Equal(InstallOutcome.NothingToInstall, result.Outcome);
    }

    [Fact]
    public async Task AnAssetOverPlainHttpIsRefused()
    {
        const string insecure = "http://example.org/TrispotQR.exe";
        _web[Feed] = Encoding.UTF8.GetBytes(FeedJson("v9.9.9", insecure, SumUrl));
        _web[insecure] = NewExe;
        var updater = Updater();

        var result = await updater.StageAsync(await FoundRelease(updater), null, TestContext.Current.CancellationToken);

        Assert.Equal(InstallOutcome.VerificationFailed, result.Outcome);
        Assert.DoesNotContain(insecure, _requested);
    }

    [Fact]
    public async Task ADownloadThatFailsLeavesNothingBehind()
    {
        var updater = Updater();
        var release = await FoundRelease(updater);
        _web.Remove(ExeUrl);

        var result = await updater.StageAsync(release, null, TestContext.Current.CancellationToken);

        Assert.Equal(InstallOutcome.DownloadFailed, result.Outcome);
        Assert.False(File.Exists(Exe + ".new"));
    }

    [Fact]
    public async Task ApplySwapsTheExeAndRestartsWithTheOldProcessId()
    {
        var updater = Updater();
        await updater.StageAsync(await FoundRelease(updater), null, TestContext.Current.CancellationToken);

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
        await updater.StageAsync(await FoundRelease(updater), null, TestContext.Current.CancellationToken);

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
        await updater.StageAsync(await FoundRelease(updater), null, TestContext.Current.CancellationToken);

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

    [Fact]
    public void WaitingForAProcessThatCannotBeOpenedReturnsWithoutThrowing()
    {
        // The replaced copy has gone and Windows gave its id to something this user may not
        // touch. This runs before the window exists, so a throw here is a crash on launch.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Process access rights are a Windows matter.");

        var started = DateTime.UtcNow;

        UpdateStartup.WaitForPredecessor([UpdateStartup.UpdatedArgument, "4"]);

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void WaitingForAnUnrelatedProcessThatReusedTheIdReturnsAtOnce()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The stand-in process is a Windows command.");

        using var unrelated = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;

        try
        {
            var started = DateTime.UtcNow;

            UpdateStartup.WaitForPredecessor([UpdateStartup.UpdatedArgument, unrelated.Id.ToString()]);

            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
        }
        finally
        {
            unrelated.Kill(entireProcessTree: true);
        }
    }

    private sealed class Collector(List<double> into) : IProgress<double>
    {
        public void Report(double value) => into.Add(value);
    }
}
