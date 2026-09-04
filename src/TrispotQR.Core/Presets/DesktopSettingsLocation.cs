using System.IO;
using System.Runtime.InteropServices;

namespace TrispotQR.Core.Presets;

/// <summary>The desktop answer: the conventional per-user configuration folder for the OS.</summary>
public sealed class DesktopSettingsLocation : ISettingsLocation
{
    public const string FolderName = "TrispotQR";

    public string Directory
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // ResolveFor is strict because a bad argument is a programming error; it fails loudly.
            // This property must degrade gracefully: when home is empty (e.g., unset HOME in a
            // container), fall back to temp rather than throwing. The app must always open,
            // even if settings are ephemeral. Losing a saved style is better than refusing to start.
            if (string.IsNullOrWhiteSpace(home))
            {
                home = Path.GetTempPath();
            }

            return ResolveFor(
                Current(),
                home,
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));
        }
    }

    /// <summary>
    /// Split out from <see cref="Directory"/> so every platform's answer can be tested from
    /// any platform, which is the only way this gets checked before Phase 3.
    /// </summary>
    public static string ResolveFor(OSPlatform platform, string home, string? appData, string? xdgConfigHome)
    {
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new ArgumentException("Home directory cannot be empty or whitespace.", nameof(home));
        }

        if (platform == OSPlatform.Windows)
        {
            var configPath = string.IsNullOrEmpty(appData) ? Path.Combine(home, "AppData", "Roaming") : appData;
            return Path.Combine(configPath, FolderName);
        }

        if (platform == OSPlatform.OSX)
        {
            return Path.Combine(home, "Library", "Application Support", FolderName);
        }

        var config = string.IsNullOrWhiteSpace(xdgConfigHome) ? Path.Combine(home, ".config") : xdgConfigHome;
        return Path.Combine(config, FolderName);
    }

    private static OSPlatform Current()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return OSPlatform.Windows;
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX : OSPlatform.Linux;
    }
}
