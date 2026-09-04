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
    public void Windows_EmptyAppData_FallsBackToHomeAppDataRoaming() =>
        Assert.True(Path.IsPathRooted(
            DesktopSettingsLocation.ResolveFor(OSPlatform.Windows, @"C:\Users\x", "", null)));

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
}
