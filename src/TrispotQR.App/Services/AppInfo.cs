using System.Reflection;

namespace TrispotQR.App.Services;

/// <summary>
/// Identity of the running build, read from the assembly rather than written down twice.
///
/// The single source of truth is &lt;Version&gt; in TrispotQR.App.csproj. Bump it there and
/// the title bar, the About window and the published file all follow. See CHANGELOG.md for
/// the release history.
/// </summary>
public static class AppInfo
{
    public const string ProductName = "Trispot QR";

    /// <summary>The version as three parts, for example "1.0.0".</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The version prefixed for display, for example "v1.0.0".</summary>
    public static string DisplayVersion => $"v{Version}";

    private static string ReadVersion()
    {
        var assembly = typeof(AppInfo).Assembly;

        // InformationalVersion carries exactly what the csproj said. It can have build
        // metadata appended after a '+', which is not worth showing anyone.
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        var version = assembly.GetName().Version;
        return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
