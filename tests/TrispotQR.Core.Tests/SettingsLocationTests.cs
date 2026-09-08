using System.IO;
using System.Runtime.InteropServices;
using TrispotQR.Core.Presets;

namespace TrispotQR.Tests;

public class SettingsLocationTests
{
    [Fact]
    public void Windows_UsesAppData() =>
        Assert.Equal(
            Path.Combine(@"C:\Users\x\AppData\Roaming", "TrispotQR"),
            DesktopSettingsLocation.ResolveFor(OSPlatform.Windows, @"C:\Users\x", @"C:\Users\x\AppData\Roaming", null));

    [Fact]
    public void Windows_EmptyAppData_FallsBackToHomeAppDataRoaming()
    {
        var resolved = DesktopSettingsLocation.ResolveFor(OSPlatform.Windows, @"C:\Users\x", "", null);

        // Deliberately NOT Path.IsPathRooted. That asks the HOST operating system what
        // "rooted" means, and a drive-lettered Windows path is not rooted on Linux or macOS,
        // where only a leading slash counts. This assertion passed on Windows and failed on
        // the other two runners for that reason alone, with nothing wrong in the code.
        //
        // The separator is normalised for the same class of reason: ResolveFor builds paths
        // with Path.Combine, which uses the host's separator, so asking for the Windows
        // answer from a Unix host yields "C:\Users\x/AppData/Roaming". That is a limit of
        // testing one platform's answer from another, not a defect. What this test needs to
        // prove is that an empty appData took the fallback and the result came from home.
        Assert.Equal("C:/Users/x/AppData/Roaming/TrispotQR", resolved.Replace('\\', '/'));
    }

    [Fact]
    public void MacOs_UsesApplicationSupport() =>
        Assert.Equal(
            "/Users/x/Library/Application Support/TrispotQR",
            DesktopSettingsLocation.ResolveFor(OSPlatform.OSX, "/Users/x", null, null).Replace('\\', '/'));

    [Fact]
    public void Linux_PrefersXdgConfigHome() =>
        Assert.Equal(
            "/home/x/.config/TrispotQR",
            DesktopSettingsLocation.ResolveFor(OSPlatform.Linux, "/home/x", null, "/home/x/.config").Replace('\\', '/'));

    [Fact]
    public void Linux_FallsBackToDotConfig() =>
        Assert.Equal(
            "/home/x/.config/TrispotQR",
            DesktopSettingsLocation.ResolveFor(OSPlatform.Linux, "/home/x", null, null).Replace('\\', '/'));

    [Fact]
    public void TheLiveLocation_IsAnAbsolutePath() =>
        Assert.True(Path.IsPathRooted(new DesktopSettingsLocation().Directory));

    [Fact]
    public void Windows_EmptyHome_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => DesktopSettingsLocation.ResolveFor(OSPlatform.Windows, "", @"C:\Users\x\AppData\Roaming", null));
        Assert.Equal("home", ex.ParamName);
    }

    [Fact]
    public void MacOs_EmptyHome_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => DesktopSettingsLocation.ResolveFor(OSPlatform.OSX, "", null, null));
        Assert.Equal("home", ex.ParamName);
    }

    [Fact]
    public void Linux_EmptyHome_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => DesktopSettingsLocation.ResolveFor(OSPlatform.Linux, "", null, "/home/x/.config"));
        Assert.Equal("home", ex.ParamName);
    }

    [Fact]
    public void Directory_NeverThrows_AndAlwaysReturnsRooted()
    {
        var location = new DesktopSettingsLocation();
        var directory = location.Directory;

        Assert.NotNull(directory);
        Assert.True(Path.IsPathRooted(directory));
    }
}
