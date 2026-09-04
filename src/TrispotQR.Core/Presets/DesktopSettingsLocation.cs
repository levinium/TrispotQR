using System.IO;
using System.Runtime.InteropServices;

namespace TrispotQR.Core.Presets;

/// <summary>The desktop answer: the conventional per-user configuration folder for the OS.</summary>
public sealed class DesktopSettingsLocation : ISettingsLocation
{
    public const string FolderName = "TrispotQR";

    public string Directory => ResolveFor(
        Current(),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));

    /// <summary>
    /// Split out from <see cref="Directory"/> so every platform's answer can be tested from
    /// any platform, which is the only way this gets checked before Phase 3.
    /// </summary>
    public static string ResolveFor(OSPlatform platform, string home, string? appData, string? xdgConfigHome)
    {
        if (platform == OSPlatform.Windows)
        {
            return Path.Combine(appData ?? Path.Combine(home, "AppData", "Roaming"), FolderName);
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
