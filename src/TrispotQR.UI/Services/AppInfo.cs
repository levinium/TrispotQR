using System.Reflection;

namespace TrispotQR.UI.Services;

/// <summary>
/// Identity of the running build, read from the assembly rather than written down twice.
///
/// The single source of truth is &lt;Version&gt; in TrispotQR.Desktop.csproj. Bump it there and
/// the version label, the About window and the published file all follow. See CHANGELOG.md for
/// the release history.
///
/// The <em>entry</em> assembly, not this one. The WPF original read typeof(AppInfo).Assembly,
/// which was the same thing there because its AppInfo lived in the executable. Here it does
/// not: this class ships in TrispotQR.UI.dll, a library with no &lt;Version&gt; of its own, so
/// reading its own assembly would report the library's default 1.0.0 forever and go on
/// reporting it after Desktop's version was bumped. The fallback covers a host with no managed
/// entry point at all; a test runner has one, so a test build reports the runner's version,
/// which is why no test asserts a particular number.
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
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;

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
