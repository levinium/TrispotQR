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

    /// <summary>The SHA-256 of the file this instance last staged and verified; null when there is none.</summary>
    private string? _stagedHash;

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

        // A new attempt supersedes whatever an earlier one vouched for.
        _stagedHash = null;

        try
        {
            // The checksum first: downloading 50 MB before finding nothing to check it against
            // wastes the time and leaves the only question that matters unanswered.
            var expected = checksum is null ? null : await ReadChecksumAsync(checksum, ct).ConfigureAwait(false);

            if (expected is null)
            {
                return new(InstallOutcome.VerificationFailed, "That release publishes no checksum, so the download cannot be verified.");
            }

            // Written under the partial name and renamed only once it matches. Another copy of
            // the app that is already Ready installs whatever carries the staged name when it
            // closes, so an unverified or half-written file must never be called that.
            var actual = await DownloadAsync(exe, plan.Partial, uiProgress, ct).ConfigureAwait(false);

            if (!UpdateAssets.Matches(expected, actual))
            {
                Discard(plan.Partial);
                return new(InstallOutcome.VerificationFailed, "The download did not match the published checksum, so it was discarded.");
            }

            File.Move(plan.Partial, plan.Staged, overwrite: true);
            _stagedHash = actual.ToLowerInvariant();

            return new(InstallOutcome.Staged);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Only the caller's own cancel. A timeout inside the HTTP client also surfaces as a
            // cancellation, and that is a download that did not finish.
            Discard(plan.Partial);
            return new(InstallOutcome.Canceled);
        }
        catch (Exception)
        {
            Discard(plan.Partial);
            return new(InstallOutcome.DownloadFailed, "The download did not finish. Nothing was changed.");
        }
    }

    /// <summary>
    /// Renames the running exe aside, renames the staged one into its place, and optionally starts
    /// it. If the second rename fails the first is undone. The window in which neither file holds
    /// the real name is one rename wide.
    ///
    /// Only a file this instance downloaded and verified is installed, and only if it still hashes
    /// the same: two copies of the app share one folder, and the staged name is not proof of
    /// anything on its own.
    /// </summary>
    public bool Apply(bool restart)
    {
        if (_exePath is null || _stagedHash is null)
        {
            return false;
        }

        var plan = UpdatePlan.For(_exePath);

        if (!UpdateAssets.Matches(_stagedHash, HashFile(plan.Staged)))
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

        _stagedHash = null;

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

    /// <summary>The lowercase SHA-256 of a file, or null when it is missing or cannot be read.</summary>
    private static string? HashFile(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return UpdateAssets.Format(SHA256.HashData(file));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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
